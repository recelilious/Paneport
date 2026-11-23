// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Net.Codecrete.QrCodeGenerator;
using Paneport.Core.Steam;
using Paneport.Core.ViewModels;
using Paneport.Tests.Fakes;
using Paneport.UI.Controls;
using Paneport.UI.Views;
using Xunit;

namespace Paneport.UI.Tests;

// S2.3.9 and S2.3.10: the Steam gate page, rendered headless over a fake service, shows every
// state of L2 steps 16 to 28, and no token, key, code or password appears in its text; the QR code
// is drawn, and drawn again when its challenge changes.
public sealed class SteamGatePageTests
{
    private const string Password = "pw-typed-into-the-box";
    private const string FirstChallenge = "https://example.invalid/q/first-challenge";
    private const string SecondChallenge = "https://example.invalid/q/second-challenge";
    private const string Code = "R7K2Q";
    private const string TokenKept = "in the keychain";

    [AvaloniaFact]
    public async Task ShowsEveryStateOfTheGateAndNoSecret()
    {
        using var warnings = AvaloniaWarnings.Record();
        var fakes = new FakeServices();
        var steps = new FakeSteamGateSteps();
        var dispatcher = new FakeUiDispatcher();
        var page = new SteamGateViewModel(fakes.Steam, steps, TokenKept, fakes.Build, fakes.Memory, dispatcher, fakes.Log);
        var (window, view) = await ShowAsync(fakes, page, dispatcher);

        // Step 16: the build, signed out, no token stored.
        Assert.Contains("Release, Mono, Full ahead-of-time, interpreter: Off", Texts(view));
        fakes.Steam.Status = new SteamSessionStatus(SteamSessionState.SignedOut, false, SteamTransport.None, null);
        Settle(window, dispatcher);
        Assert.Equal("Signed out", Text(view, "Session"));
        Assert.Equal("no", Text(view, "TokenStored"));
        Assert.Equal(TokenKept, Text(view, "TokenKept"));

        // Step 17: sign in by approval; the password box empties at once.
        view.FindControl<TextBox>("AccountName")!.Text = "sample-name";
        view.FindControl<TextBox>("Password")!.Text = Password;
        Settle(window, dispatcher);
        AssertShowsNoSecret(view);
        var signIn = page.SignInCommand.ExecuteAsync(null);
        Settle(window, dispatcher);
        Assert.Equal(string.Empty, view.FindControl<TextBox>("Password")!.Text);
        var attempt = fakes.Steam.LastAttempt!;
        attempt.Show(new SignInProgress(SignInStage.WaitingForApproval));
        Settle(window, dispatcher);
        Assert.Equal("Approve the sign-in in the Steam mobile app, then come back.", Text(view, "SignInStage"));
        Assert.True(view.FindControl<Button>("UseCodeInstead")!.IsEffectivelyVisible);
        Assert.True(view.FindControl<Button>("CancelSignIn")!.IsEffectivelyVisible);
        AssertShowsNoSecret(view);
        attempt.End(new SignInResult(SignInOutcome.LoggedOn, TimeSpan.FromSeconds(14.2), TimeSpan.FromSeconds(9.8), null));
        await signIn;
        fakes.Steam.Status = new SteamSessionStatus(SteamSessionState.LoggedOn, true, SteamTransport.WebSocket, null);
        Settle(window, dispatcher);
        Assert.Equal("Logged on", Text(view, "Session"));
        Assert.Equal("yes", Text(view, "TokenStored"));
        Assert.Equal("WebSocket", Text(view, "Transport"));
        Assert.Equal("Logged on with an approval: total 14.2 s, waiting for you 9.8 s.", Text(view, "SignInTimes"));
        Assert.False(view.FindControl<Button>("CancelSignIn")!.IsEffectivelyVisible);

        // Step 18: sign out.
        await page.SignOutCommand.ExecuteAsync(null);
        Settle(window, dispatcher);
        Assert.Equal("no", Text(view, "TokenStored"));

        // Step 19: sign in with a code.
        page.Password = Password;
        page.EnterCodeInstead = true;
        signIn = page.SignInCommand.ExecuteAsync(null);
        attempt = fakes.Steam.LastAttempt!;
        attempt.Show(new SignInProgress(SignInStage.WaitingForCode, SteamCodeKind.Authenticator));
        Settle(window, dispatcher);
        Assert.Equal("Enter the Steam Guard code from the Steam mobile app.", Text(view, "SignInStage"));
        Assert.True(view.FindControl<TextBox>("Code")!.IsEffectivelyVisible);
        AssertShowsNoSecret(view);
        view.FindControl<TextBox>("Code")!.Text = Code;
        Settle(window, dispatcher);
        AssertShowsNoSecret(view);
        page.SubmitCodeCommand.Execute(null);
        Settle(window, dispatcher);
        Assert.Equal(string.Empty, view.FindControl<TextBox>("Code")!.Text);
        attempt.End(new SignInResult(SignInOutcome.LoggedOn, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(25), null));
        await signIn;
        Settle(window, dispatcher);
        Assert.Equal("Logged on with a code: total 30.0 s, waiting for you 25.0 s.", Text(view, "SignInTimes"));

        // Step 20: sign in with a QR code, which is drawn again when the challenge changes.
        signIn = page.SignInWithQrCodeCommand.ExecuteAsync(null);
        attempt = fakes.Steam.LastAttempt!;
        attempt.Show(new SignInProgress(SignInStage.ShowingQrCode, QrChallenge: FirstChallenge));
        Settle(window, dispatcher);
        var qr = view.FindControl<QrCodeView>("QrCode")!;
        Assert.True(qr.IsEffectivelyVisible);
        Assert.Equal(DarkModules(FirstChallenge), DrawnModules(qr));
        AssertShowsNoSecret(view);
        attempt.Show(new SignInProgress(SignInStage.ShowingQrCode, QrChallenge: SecondChallenge, QrRevision: 1));
        Settle(window, dispatcher);
        Assert.Equal(SecondChallenge, qr.Text);
        Assert.Equal(DarkModules(SecondChallenge), DrawnModules(qr));
        AssertShowsNoSecret(view);
        attempt.End(new SignInResult(SignInOutcome.LoggedOn, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(18), null));
        await signIn;
        Settle(window, dispatcher);
        Assert.False(qr.IsEffectivelyVisible);
        Assert.Equal("Logged on with a QR code: total 20.0 s, waiting for you 18.0 s.", Text(view, "SignInTimes"));

        // Steps 22 to 26: licenses, app information, depot key and manifest code, manifest, chunks.
        await page.CountLicensesCommand.ExecuteAsync(null);
        view.FindControl<TextBox>("AppId")!.Text = "4100000100";
        await page.ReadAppInfoCommand.ExecuteAsync(null);
        await page.GetDepotKeyCommand.ExecuteAsync(null);
        await page.GetManifestCodeCommand.ExecuteAsync(null);
        await page.DownloadManifestCommand.ExecuteAsync(null);
        await page.DownloadChunksCommand.ExecuteAsync(null);
        Settle(window, dispatcher);
        Assert.Equal("42 licenses", Text(view, "LicensesResult"));
        Assert.Equal("Lantern Test App, 2 Windows depots.", Text(view, "AppInfoResult"));
        Assert.Equal(2, view.FindControl<ComboBox>("Depots")!.ItemCount);
        Assert.Equal("depot key: obtained", Text(view, "DepotKeyResult"));
        Assert.Equal("manifest request code: obtained", Text(view, "ManifestCodeResult"));
        Assert.Equal("12 files, 30 chunks, 2,048 bytes, downloaded in 0.9 s", Text(view, "ManifestResult"));
        Assert.StartsWith("10 verified, 0 rejected, 0 failed of 10;", Text(view, "ChunksResult"), StringComparison.Ordinal);

        // Step 27: the summary, with the permission prompt the tester saw.
        view.FindControl<CheckBox>("PermissionPrompt")!.IsChecked = true;
        Settle(window, dispatcher);
        var summaryBlock = view.FindControl<SelectableTextBlock>("Summary")!;
        var summary = summaryBlock.Text!;
        foreach (var line in new[] { "Sign-in with an approval", "Sign-in with a code", "Sign-in with a QR code", "Licenses: 42", "Manifest: 12 files", "Chunks: 10 verified", $"kept: {TokenKept}", "transport: WebSocket", "System permission prompt seen: yes" })
        {
            Assert.Contains(line, summary, StringComparison.Ordinal);
        }

        // The summary uses the timeline style, monospace, like the Diagnostics timeline.
        Assert.Equal("monospace", summaryBlock.FontFamily.Name);
        Assert.Equal(12, summaryBlock.FontSize);

        // Step 28: delete the test data, then sign out.
        await page.DeleteTestDataCommand.ExecuteAsync(null);
        await page.SignOutCommand.ExecuteAsync(null);
        Settle(window, dispatcher);
        Assert.Equal("The test data was deleted.", Text(view, "ChunksResult"));
        Assert.Equal("no", Text(view, "TokenStored"));

        // At the end, too, no text of the page holds the password, the code or a challenge.
        AssertShowsNoSecret(view);
        Assert.DoesNotContain(fakes.Log.Lines, line => line.Message.Contains(Password, StringComparison.Ordinal));
        Assert.Empty(warnings.Entries);
        window.Close();
    }

    // The keyboard takes its options from the focused box, and Avalonia.iOS makes a secure text
    // field only for the Password content type or a sensitive box: PasswordChar masks only what the
    // box draws. A secure field neither suggests, learns nor changes what is typed, and iOS allows
    // no third-party keyboard in it. Suggestions stay off in the other two boxes too.
    [AvaloniaFact]
    public async Task TheSignInBoxesAskForASecureKeyboardWithoutSuggestions()
    {
        using var warnings = AvaloniaWarnings.Record();
        var fakes = new FakeServices();
        var dispatcher = new FakeUiDispatcher();
        var page = new SteamGateViewModel(fakes.Steam, new FakeSteamGateSteps(), TokenKept, fakes.Build, fakes.Memory, dispatcher, fakes.Log);
        var (window, view) = await ShowAsync(fakes, page, dispatcher);

        var password = view.FindControl<TextBox>("Password")!;
        Assert.Equal(TextInputContentType.Password, TextInputOptions.GetContentType(password));
        Assert.True(TextInputOptions.GetIsSensitive(password));
        foreach (var name in new[] { "AccountName", "Password", "Code" })
        {
            Assert.False(TextInputOptions.GetShowSuggestions(view.FindControl<TextBox>(name)!));
        }

        Assert.Empty(warnings.Entries);
        window.Close();
    }

    [AvaloniaFact]
    public void TheQrCodeHasTheModulesOfItsText()
    {
        using var warnings = AvaloniaWarnings.Record();
        var view = new QrCodeView();
        var window = new Window { Width = 400, Height = 400, Content = view };
        window.Show();

        view.Text = FirstChallenge;
        window.UpdateLayout();

        var expected = QrCode.EncodeText(FirstChallenge, QrCode.Ecc.Medium);
        Assert.Equal(expected.Size, view.ModuleCount);
        for (var y = 0; y < expected.Size; y++)
        {
            for (var x = 0; x < expected.Size; x++)
            {
                Assert.Equal(expected.GetModule(x, y), view.IsDark(x, y));
            }
        }

        // What the control draws is the code of its latest text. That a frame is drawn again when
        // the text changes (AffectsRender) would need a frame rendered with Skia; headless drawing
        // renders none.
        Assert.Equal(DarkModules(FirstChallenge), DrawnModules(view));
        view.Text = SecondChallenge;
        window.UpdateLayout();
        Assert.Equal(DarkModules(SecondChallenge), DrawnModules(view));
        view.Text = null;
        Assert.Equal(0, view.ModuleCount);
        Assert.Empty(DrawnModules(view));
        Assert.Empty(warnings.Entries);
        window.Close();
    }

    // The dark modules of a text's QR code, by column and row.
    private static HashSet<(int X, int Y)> DarkModules(string text)
    {
        var code = QrCode.EncodeText(text, QrCode.Ecc.Medium);
        var modules = new HashSet<(int X, int Y)>();
        for (var y = 0; y < code.Size; y++)
        {
            for (var x = 0; x < code.Size; x++)
            {
                if (code.GetModule(x, y))
                {
                    modules.Add((x, y));
                }
            }
        }

        return modules;
    }

    // Draws the control into a drawing group, as a frame would, and returns the modules that it
    // filled black, by column and row inside the quiet zone. The first drawing is the white square
    // of the code with its quiet zone.
    private static HashSet<(int X, int Y)> DrawnModules(QrCodeView view)
    {
        var group = new DrawingGroup();
        using (var context = group.Open())
        {
            view.Render(context);
        }

        var drawings = group.Children.OfType<GeometryDrawing>().ToList();
        var modules = new HashSet<(int X, int Y)>();
        if (drawings.Count == 0)
        {
            return modules;
        }

        Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(drawings[0].Brush).Color);
        var square = drawings[0].Geometry!.Bounds;
        var size = square.Width / (view.ModuleCount + (2 * QrCodeView.QuietZone));
        foreach (var drawing in drawings.Skip(1))
        {
            Assert.Equal(Colors.Black, Assert.IsAssignableFrom<ISolidColorBrush>(drawing.Brush).Color);
            var module = drawing.Geometry!.Bounds;
            Assert.Equal(size, module.Width);
            Assert.True(modules.Add((
                (int)Math.Round((module.X - square.X) / size) - QrCodeView.QuietZone,
                (int)Math.Round((module.Y - square.Y) / size) - QrCodeView.QuietZone)));
        }

        return modules;
    }

    // No text that the page shows holds the password, the code or a QR challenge. The page test
    // checks this at every stage where one of them exists, not only at the end.
    private static void AssertShowsNoSecret(SteamGatePage view)
    {
        var all = string.Join('\n', Texts(view));
        Assert.DoesNotContain(Password, all, StringComparison.Ordinal);
        Assert.DoesNotContain(Code, all, StringComparison.Ordinal);
        Assert.DoesNotContain("example.invalid", all, StringComparison.Ordinal);
    }

    // Opens the page on top of the shell, as the developer menu does, which loads it and tells it
    // that it is shown.
    private static async Task<(Window Window, SteamGatePage View)> ShowAsync(FakeServices fakes, SteamGateViewModel page, FakeUiDispatcher dispatcher)
    {
        var shell = new ShellViewModel(fakes.ToAppServices());
        var main = new MainView { DataContext = shell };
        var window = new Window { Width = 1024, Height = 1600, Content = main };
        window.Show();
        await shell.PushAsync(page, TestContext.Current.CancellationToken);
        Settle(window, dispatcher);
        return (window, main.GetVisualDescendants().OfType<SteamGatePage>().Single());
    }

    // Runs the page's queued updates and Avalonia's jobs, and lays out the window.
    private static void Settle(Window window, FakeUiDispatcher dispatcher)
    {
        dispatcher.RunPending();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static string? Text(SteamGatePage view, string name) => view.FindControl<TextBlock>(name)?.Text;

    private static List<string> Texts(SteamGatePage view) =>
        view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && text.Text is not null)
            .Select(text => text.Text!)
            .ToList();
}
