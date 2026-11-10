// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Reflection;
using Xunit;

namespace Paneport.Interop.Tests;

// The interface conformance test of C-0005 section 5, with a planted wrong definition that it
// must report by struct and member. The device test flavor runs the same class on the device.
public sealed class LayoutConformanceTests
{
    [Fact]
    public void EveryStructMatchesTheHost()
    {
        Assert.Empty(LayoutConformance.Check());
    }

    // ManagedLayouts is a list written by hand. Every blittable struct of the bindings, named
    // Native*, must be in it with every field, or the comparison would miss it.
    [Fact]
    public void TheBindingsListEveryNativeStructAndField()
    {
        var structs = typeof(NativeHost).Assembly.GetTypes()
            .Where(type => type.IsValueType && !type.IsEnum && type.Name.StartsWith("Native", StringComparison.Ordinal))
            .ToList();
        var layouts = LayoutConformance.ManagedLayouts();

        Assert.Equal(structs.Count, layouts.Count);
        Assert.Equal(
            structs.Sum(type => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length),
            layouts.Sum(layout => layout.Members.Count));
    }

    [Fact]
    public void TheHostListsEveryStructOfTheBindings()
    {
        var native = HostTestExports.ReadLayoutTable();

        Assert.Equal(
            LayoutConformance.ManagedLayouts().Select(layout => layout.Name).Order(),
            native.Select(layout => layout.Name).Order());
        var budget = Assert.Single(native, layout => layout.Name == "pp_memory_budget_t");
        Assert.Equal(32, budget.Size);
        Assert.Equal(
            ["struct_size", "game_mode_level_reached", "limit_bytes", "available_bytes", "footprint_bytes"],
            budget.Members.Select(member => member.Name));
    }

    [Fact]
    public void APlantedWrongDefinitionIsReportedWithItsStructAndMembers()
    {
        var wrong = new StructLayoutBuilder<WrongMemoryBudget>("pp_memory_budget_t")
            .Member("struct_size", (ref WrongMemoryBudget value) => ref value.StructSize)
            .Member("game_mode_level_reached", (ref WrongMemoryBudget value) => ref value.GameModeLevelReached)
            .Member("limit_bytes", (ref WrongMemoryBudget value) => ref value.LimitBytes)
            .Member("available_bytes", (ref WrongMemoryBudget value) => ref value.AvailableBytes)
            .Member("footprint_bytes", (ref WrongMemoryBudget value) => ref value.FootprintBytes)
            .Build();
        var native = HostTestExports.ReadLayoutTable().Where(layout => layout.Name == "pp_memory_budget_t").ToList();

        var problems = LayoutConformance.Compare([wrong], native);

        Assert.Equal(
            [
                "pp_memory_budget_t: 40 bytes in C#, 32 bytes in C.",
                "pp_memory_budget_t.game_mode_level_reached: offset 16 and size 1 in C#, offset 4 and size 1 in C.",
                "pp_memory_budget_t.available_bytes: offset 24 and size 8 in C#, offset 16 and size 8 in C.",
                "pp_memory_budget_t.footprint_bytes: offset 32 and size 8 in C#, offset 24 and size 8 in C.",
            ],
            problems);
    }

    [Fact]
    public void MissingAndExtraStructsAndMembersAreReported()
    {
        StructLayoutRecord[] native =
        [
            new("pp_a_t", 8, [new("first", 0, 4), new("second", 4, 4)]),
            new("pp_b_t", 4, [new("only", 0, 4)]),
        ];
        StructLayoutRecord[] managed =
        [
            new("pp_a_t", 8, [new("first", 0, 4), new("third", 4, 4)]),
            new("pp_c_t", 4, [new("only", 0, 4)]),
        ];

        Assert.Equal(
            [
                "pp_a_t.second: missing in C#.",
                "pp_a_t.third: only in C#.",
                "pp_b_t: the host defines it, but Paneport.Interop does not.",
                "pp_c_t: only in C#.",
            ],
            LayoutConformance.Compare(managed, native));
    }
}
