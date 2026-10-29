// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core;

// Planted for the M0.2 check run: a reflection call that the trimming analyzers reject.
internal static class PlantedReflection
{
    internal static object? Create(string typeName) => Activator.CreateInstance(Type.GetType(typeName)!);
}
