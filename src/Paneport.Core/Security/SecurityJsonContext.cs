// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

namespace Paneport.Core.Security;

// Source-generated serialization of the sign-in item, with no reflection (C-0010 section 2).
[JsonSerializable(typeof(SignInItem))]
internal sealed partial class SecurityJsonContext : JsonSerializerContext;
