// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Compares the installed toolchain with its pins (C-0009 section 2): the .NET SDK with
// global.json and, when asked, the CI tools with eng/toolchain.json.
//
//   dotnet run tools/CheckToolchain.cs                  check the SDK
//   dotnet run tools/CheckToolchain.cs -- --ci-tools    check the SDK and every pinned CI tool
//   dotnet run tools/CheckToolchain.cs -- --self-test   check the parsers on synthetic outputs
//
// Exit codes: 0 everything matches its pin, 1 something does not, 2 usage or configuration
// error. docs/design/repository-checks.md describes the pins and how to change one.

#:property PublishAot=false

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

try
{
    return args switch
    {
        [] => ToolchainCheck.Run(ciTools: false),
        ["--ci-tools"] => ToolchainCheck.Run(ciTools: true),
        ["--self-test"] => SelfTest.Run(),
        _ => Cli.Usage(),
    };
}
catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 2;
}

internal static class Cli
{
    public static int Usage()
    {
        Console.Error.WriteLine("usage: dotnet run tools/CheckToolchain.cs [-- --ci-tools | --self-test]");
        return 2;
    }
}

// What a command printed. A runner returns null when the command could not be started.
internal sealed record CommandResult(int ExitCode, string Output, string Error);

internal delegate CommandResult? CommandRunner(string command, string[] arguments);

internal sealed record SdkPin(string Version, string RollForward);

internal sealed record Download(string Runtime, string Url, string Sha256);

internal sealed record ToolPin(string Name, string Version, string Source, List<Download> Downloads);

internal static class GlobalJson
{
    public const string FilePath = "global.json";

    public static SdkPin Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("sdk", out var sdk)
                || sdk.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{FilePath} has no \"sdk\" object.");
            }

            var version = Pins.Text(sdk, "version", FilePath);
            var rollForward = sdk.TryGetProperty("rollForward", out var property)
                && property.ValueKind == JsonValueKind.String
                    ? property.GetString()!
                    : "(not set)";
            return new SdkPin(version, rollForward);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{FilePath} is not valid JSON: {exception.Message}");
        }
    }
}

// eng/toolchain.json: each tool has its exact version and the source it is installed from.
// A tool that CI downloads has, per runtime identifier, the download's address and SHA-256.
// The file holds nothing but "tools", and no field beyond these, so that no pin escapes the
// version check here and the register check of tools/CheckLicenses.cs.
internal static partial class ToolchainJson
{
    public const string FilePath = "eng/toolchain.json";

    private static readonly string[] s_toolFields = ["version", "source", "downloads"];
    private static readonly string[] s_downloadFields = ["url", "sha256"];

    public static List<ToolPin> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("tools", out var tools)
                || tools.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{FilePath} has no \"tools\" object.");
            }

            var other = root.EnumerateObject().Select(key => key.Name).FirstOrDefault(name => name != "tools");
            if (other is not null)
            {
                throw new InvalidDataException(
                    $"{FilePath}: \"{other}\" is not \"tools\". tools/CheckToolchain.cs and tools/CheckLicenses.cs "
                        + "read only \"tools\", so a pin elsewhere would go unchecked.");
            }

            return tools.EnumerateObject().Select(tool => ParseTool(tool.Name, tool.Value)).ToList();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{FilePath} is not valid JSON: {exception.Message}");
        }
    }

    private static ToolPin ParseTool(string name, JsonElement tool)
    {
        var where = $"{FilePath}, tool {name}";
        if (tool.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{where}: not an object.");
        }

        var unknown = tool.EnumerateObject()
            .Select(field => field.Name)
            .FirstOrDefault(field => !s_toolFields.Contains(field));
        if (unknown is not null)
        {
            throw new InvalidDataException($"{where}: unknown field \"{unknown}\".");
        }

        var downloads = new List<Download>();
        if (tool.TryGetProperty("downloads", out var runtimes))
        {
            if (runtimes.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{where}: \"downloads\" is not an object.");
            }

            foreach (var runtime in runtimes.EnumerateObject())
            {
                var field = runtime.Value.ValueKind == JsonValueKind.Object
                    ? runtime.Value.EnumerateObject()
                        .Select(property => property.Name)
                        .FirstOrDefault(name => !s_downloadFields.Contains(name))
                    : throw new InvalidDataException($"{where}: the download for {runtime.Name} is not an object.");
                if (field is not null)
                {
                    throw new InvalidDataException(
                        $"{where}: unknown field \"{field}\" in the download for {runtime.Name}.");
                }

                var download = new Download(
                    runtime.Name,
                    Https(Pins.Text(runtime.Value, "url", where), where),
                    Pins.Text(runtime.Value, "sha256", where));
                downloads.Add(Sha256().IsMatch(download.Sha256)
                    ? download
                    : throw new InvalidDataException(
                        $"{where}: the sha256 of {runtime.Name} is not 64 lowercase hex digits."));
            }
        }

        var version = Pins.Text(tool, "version", where);
        return new ToolPin(name, version, Https(Pins.Text(tool, "source", where), where), downloads);
    }

    private static string Https(string url, string where) => url.StartsWith("https://", StringComparison.Ordinal)
        ? url
        : throw new InvalidDataException($"{where}: \"{url}\" is not an https address.");

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();
}

internal static class Pins
{
    public static string Text(JsonElement element, string name, string where)
    {
        var value = element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"{where}: \"{name}\" is missing or empty.")
            : value;
    }
}

// How to ask each pinned tool for its version, and how to read the answer. A tool pinned in
// eng/toolchain.json without an entry here fails the check, so no pin goes unchecked.
internal sealed record VersionProbe(string Command, string[] Arguments, Regex Pattern);

internal static partial class Probes
{
    public static readonly Dictionary<string, VersionProbe> ByTool = new(StringComparer.Ordinal)
    {
        ["reuse"] = new("reuse", ["--version"], ReuseVersion()),
        ["gitleaks"] = new("gitleaks", ["version"], GitleaksVersion()),
        ["lychee"] = new("lychee", ["--version"], LycheeVersion()),
    };

    // "reuse, version 6.2.0", followed by the license notice.
    [GeneratedRegex(@"\Areuse, version ([0-9][^\s]*)")]
    private static partial Regex ReuseVersion();

    // "8.30.1", or "v8.30.1" in older releases.
    [GeneratedRegex(@"\Av?([0-9][^\s]*)\z")]
    private static partial Regex GitleaksVersion();

    // "lychee 0.24.2".
    [GeneratedRegex(@"\Alychee ([0-9][^\s]*)")]
    private static partial Regex LycheeVersion();
}

internal static class Checks
{
    // Returns the problems; an empty list means that the SDK matches its pin.
    public static List<string> Sdk(SdkPin pin, CommandResult? dotnetVersion)
    {
        var problems = new List<string>();
        if (pin.RollForward != "disable")
        {
            problems.Add(
                $"{GlobalJson.FilePath} sets rollForward to {pin.RollForward}. The pin is exact (C-0009 section 2), "
                    + "so it must be \"disable\".");
        }

        if (dotnetVersion is null)
        {
            problems.Add("dotnet could not be started.");
        }
        else if (dotnetVersion.ExitCode != 0)
        {
            problems.Add(
                $"dotnet --version exited with {dotnetVersion.ExitCode}, which usually means that the SDK "
                    + $"{pin.Version} is not installed: {FirstLine(dotnetVersion.Error + dotnetVersion.Output)}");
        }
        else if (dotnetVersion.Output.Trim() != pin.Version)
        {
            problems.Add(
                $"the SDK in use is {dotnetVersion.Output.Trim()}, but {GlobalJson.FilePath} pins {pin.Version}.");
        }

        return problems;
    }

    public static List<string> Tool(ToolPin pin, CommandRunner run)
    {
        if (!Probes.ByTool.TryGetValue(pin.Name, out var probe))
        {
            return [$"{ToolchainJson.FilePath} pins {pin.Name}, but tools/CheckToolchain.cs cannot check it yet."];
        }

        var command = $"{probe.Command} {string.Join(' ', probe.Arguments)}";
        var result = run(probe.Command, probe.Arguments);
        if (result is null)
        {
            return [$"{pin.Name} is not installed, or not on the PATH."];
        }

        if (result.ExitCode != 0)
        {
            return [$"{command} exited with {result.ExitCode}: {FirstLine(result.Error + result.Output)}"];
        }

        var match = probe.Pattern.Match(result.Output.Trim());
        if (!match.Success)
        {
            return [$"{command} printed no version that this tool can read: {FirstLine(result.Output)}"];
        }

        var installed = match.Groups[1].Value;
        return installed == pin.Version
            ? []
            : [$"{pin.Name} {installed} is installed, but {ToolchainJson.FilePath} pins {pin.Version}."];
    }

    private static string FirstLine(string text) =>
        text.Trim().Split('\n').FirstOrDefault(line => line.Trim().Length > 0)?.Trim() ?? "(no output)";
}

internal static class ToolchainCheck
{
    public static int Run(bool ciTools)
    {
        var root = Processes.Run("git", ["rev-parse", "--show-toplevel"], Directory.GetCurrentDirectory());
        if (root is null || root.ExitCode != 0)
        {
            throw new InvalidDataException("run this tool inside the repository.");
        }

        // Commands run at the repository root, where global.json selects the SDK.
        var directory = root.Output.Trim();
        CommandRunner runner = (command, arguments) => Processes.Run(command, arguments, directory);

        Console.WriteLine("Toolchain pins (C-0009 section 2)");
        var failed = 0;
        var sdk = GlobalJson.Parse(Read(directory, GlobalJson.FilePath));
        failed += Report(
            $".NET SDK {sdk.Version} from {GlobalJson.FilePath}", Checks.Sdk(sdk, runner("dotnet", ["--version"])));

        // The pin file is read on every run, so that a local run finds a broken pin too.
        var pins = ToolchainJson.Parse(Read(directory, ToolchainJson.FilePath));
        if (ciTools)
        {
            foreach (var tool in pins)
            {
                var pin = $"{tool.Name} {tool.Version} from {ToolchainJson.FilePath}";
                failed += Report(pin, Checks.Tool(tool, runner));
            }
        }
        else
        {
            Console.WriteLine(
                $"  The {pins.Count} CI tools in {ToolchainJson.FilePath} were read, not run; --ci-tools runs them.");
        }

        Console.WriteLine(failed == 0 ? "Everything checked matches its pin." : $"{failed} pins do not match.");
        return failed == 0 ? 0 : 1;
    }

    private static string Read(string directory, string file)
    {
        var path = Path.Combine(directory, file);
        return File.Exists(path) ? File.ReadAllText(path) : throw new InvalidDataException($"{file} does not exist.");
    }

    private static int Report(string pin, List<string> problems)
    {
        Console.WriteLine($"  {(problems.Count == 0 ? "ok  " : "FAIL")} {pin}");
        foreach (var problem in problems)
        {
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            {
                Console.WriteLine($"::error title=Toolchain pins::{problem}");
            }

            Console.WriteLine($"       {problem}");
        }

        return problems.Count == 0 ? 0 : 1;
    }
}

internal static class Processes
{
    public static CommandResult? Run(string command, string[] arguments, string workingDirectory)
    {
        var start = new ProcessStartInfo(command)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return new CommandResult(process.ExitCode, output, error.Result);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}

internal sealed class Tally
{
    public int Total { get; private set; }

    public int Failed { get; private set; }

    public void Expect(bool passed, string description)
    {
        Total++;
        Failed += passed ? 0 : 1;
        Console.WriteLine($"  {(passed ? "ok  " : "FAIL")} {description}");
    }
}

// Every fixture is a synthetic version output or pin file (C-0011 section 1).
internal static class SelfTest
{
    private const string Pinned =
        """
        {
          "tools": {
            "reuse": { "version": "6.2.0", "source": "https://example.com/reuse" },
            "gitleaks": {
              "version": "8.30.1",
              "source": "https://example.com/gitleaks",
              "downloads": {
                "linux-x64": {
                  "url": "https://example.com/gitleaks.tar.gz",
                  "sha256": "0000000000000000000000000000000000000000000000000000000000000000"
                }
              }
            },
            "lychee": { "version": "0.24.2", "source": "https://example.com/lychee" }
          }
        }
        """;

    private const string ReuseOutput =
        """
        reuse, version 6.2.0

        This program is free software: you can redistribute it and/or modify it under the terms of the
        GNU General Public License as published by the Free Software Foundation, either version 3 of the
        License, or (at your option) any later version.
        """;

    public static int Run()
    {
        Console.WriteLine("Self-test of tools/CheckToolchain.cs, with synthetic outputs only.");
        var tally = new Tally();

        Console.WriteLine("SDK:");
        var sdk = GlobalJson.Parse("""{ "sdk": { "version": "10.0.403", "rollForward": "disable" } }""");
        tally.Expect(sdk == new SdkPin("10.0.403", "disable"), "global.json yields the version and roll-forward");
        tally.Expect(Checks.Sdk(sdk, new CommandResult(0, "10.0.403\n", "")).Count == 0, "the pinned SDK passes");
        tally.Expect(Checks.Sdk(sdk, new CommandResult(0, "10.0.404\n", "")).Count == 1, "another SDK fails");
        tally.Expect(
            Checks.Sdk(sdk, new CommandResult(145, "", "A compatible .NET SDK was not found.")).Count == 1,
            "a missing SDK fails");
        tally.Expect(Checks.Sdk(sdk, null).Count == 1, "a missing dotnet fails");
        tally.Expect(
            Checks.Sdk(sdk with { RollForward = "latestFeature" }, new CommandResult(0, "10.0.403", "")).Count == 1,
            "a pin that may roll forward fails");
        tally.Expect(Rejects(() => GlobalJson.Parse("""{ "sdk": { } }""")), "global.json without a version fails");
        tally.Expect(Rejects(() => GlobalJson.Parse("[]")), "global.json that is no object fails");

        Console.WriteLine("Pin file:");
        var pins = ToolchainJson.Parse(Pinned);
        string[] expected = ["reuse 6.2.0", "gitleaks 8.30.1", "lychee 0.24.2"];
        tally.Expect(
            pins.Select(pin => $"{pin.Name} {pin.Version}").SequenceEqual(expected),
            "every tool and its version are read");
        tally.Expect(pins[1].Downloads.Single().Runtime == "linux-x64", "a download is read with its runtime");
        tally.Expect(Rejects(() => ToolchainJson.Parse(Edit(Pinned, "000\"", "00\""))), "a short SHA-256 is rejected");
        tally.Expect(
            Rejects(() => ToolchainJson.Parse(Edit(Pinned, "https://example.com/gitleaks.tar.gz", "http://x"))),
            "a download that is not https is rejected");
        tally.Expect(
            Rejects(() => ToolchainJson.Parse(Edit(Pinned, "\"version\": \"0.24.2\", ", ""))),
            "a tool without a version is rejected");
        tally.Expect(
            Rejects(() => ToolchainJson.Parse(Edit(Pinned, "\"6.2.0\",", "\"6.2.0\", \"x\": \"y\","))),
            "an unknown field is rejected");
        tally.Expect(
            Rejects(() => ToolchainJson.Parse(Edit(Pinned, "\"sha256\":", "\"sha512\": \"x\", \"sha256\":"))),
            "an unknown field in a download is rejected");
        tally.Expect(
            Rejects(() => ToolchainJson.Parse(Edit(Pinned, "\"tools\": {", "\"xcode\": {}, \"tools\": {"))),
            "a pin beside \"tools\" is rejected");
        tally.Expect(
            Rejects(() => ToolchainJson.Parse(
                """{ "tools": { "x": { "version": "1.0", "source": "https://example.com/", "downloads": [] } } }""")),
            "downloads that are no object are rejected");
        tally.Expect(Rejects(() => ToolchainJson.Parse("[]")), "a pin file that is no object is rejected");

        Console.WriteLine("Tool versions:");
        var outputs = new Dictionary<string, CommandResult>
        {
            ["reuse"] = new(0, ReuseOutput, ""),
            ["gitleaks"] = new(0, "8.30.1\n", ""),
            ["lychee"] = new(0, "lychee 0.24.2\n", ""),
        };
        CommandRunner installed = (command, _) => outputs.GetValueOrDefault(command);
        tally.Expect(pins.All(pin => Checks.Tool(pin, installed).Count == 0), "reuse, gitleaks and lychee match");
        tally.Expect(
            Checks.Tool(pins[1], (_, _) => new CommandResult(0, "v8.30.1", "")).Count == 0,
            "a gitleaks version with a leading v is read");
        tally.Expect(
            Checks.Tool(pins[2], (_, _) => new CommandResult(0, "lychee 0.24.1", "")).Count == 1,
            "another lychee version fails");
        tally.Expect(
            Checks.Tool(pins[0], (_, _) => new CommandResult(0, "usage: reuse [OPTIONS]", "")).Count == 1,
            "output without a version fails");
        tally.Expect(
            Checks.Tool(pins[0], (_, _) => new CommandResult(2, "", "unknown option")).Count == 1,
            "a version command that fails fails");
        tally.Expect(Checks.Tool(pins[1], (_, _) => null).Count == 1, "a tool that is not installed fails");
        tally.Expect(
            Checks.Tool(new ToolPin("unchecked-tool", "1.0.0", "https://example.com/", []), installed).Count == 1,
            "a pinned tool without a version check fails");

        Console.WriteLine(tally.Failed == 0
            ? $"Self-test passed: {tally.Total} of {tally.Total} checks as expected."
            : $"Self-test FAILED: {tally.Failed} of {tally.Total} checks not as expected.");
        return tally.Failed == 0 ? 0 : 1;
    }

    private static string Edit(string text, string from, string to) => text.Replace(from, to, StringComparison.Ordinal);

    private static bool Rejects(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }
}
