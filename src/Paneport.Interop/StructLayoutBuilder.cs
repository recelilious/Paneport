// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Paneport.Interop;

/// <summary>
/// Measures the layout of a C# struct as the runtime lays it out in memory, which is the
/// layout that crosses the interface, because runtime marshalling is off and the structs are
/// blittable (C-0005 section 1).
/// </summary>
/// <typeparam name="TStruct">The struct to measure.</typeparam>
/// <remarks>
/// Each offset is the distance in bytes from the start of a struct to the field that the
/// accessor returns, and each size is the field type's size. Marshal.OffsetOf would describe
/// the marshalled layout instead, which this assembly never uses.
/// </remarks>
/// <param name="name">The C type name that the struct stands for, such as <c>pp_event_t</c>.</param>
public sealed class StructLayoutBuilder<TStruct>(string name)
    where TStruct : unmanaged
{
    private readonly List<MemberLayoutRecord> _members = [];

    /// <summary>Adds a member, measured on a struct of default values.</summary>
    /// <typeparam name="TField">The member's type.</typeparam>
    /// <param name="memberName">The member's name as the C header spells it.</param>
    /// <param name="field">Returns a reference to the member.</param>
    /// <returns>This builder.</returns>
    public StructLayoutBuilder<TStruct> Member<TField>(string memberName, FieldAccessor<TStruct, TField> field)
        where TField : unmanaged
    {
        ArgumentNullException.ThrowIfNull(field);
        var value = default(TStruct);
        ref var start = ref Unsafe.As<TStruct, byte>(ref value);
        ref var member = ref Unsafe.As<TField, byte>(ref field(ref value));
        var offset = (int)Unsafe.ByteOffset(ref start, ref member);
        _members.Add(new MemberLayoutRecord(memberName, offset, Unsafe.SizeOf<TField>()));
        return this;
    }

    /// <summary>Returns the measured layout.</summary>
    /// <returns>The struct's size and its members, in the order they were added.</returns>
    public StructLayoutRecord Build() => new(name, Unsafe.SizeOf<TStruct>(), [.. _members]);
}
