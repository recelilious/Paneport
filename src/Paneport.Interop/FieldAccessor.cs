// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Interop;

/// <summary>Returns a reference to one field of a struct, so that its offset can be measured.</summary>
/// <typeparam name="TStruct">The struct.</typeparam>
/// <typeparam name="TField">The field's type.</typeparam>
/// <param name="value">The struct whose field is wanted.</param>
/// <returns>A reference to the field inside <paramref name="value"/>.</returns>
public delegate ref TField FieldAccessor<TStruct, TField>(ref TStruct value);
