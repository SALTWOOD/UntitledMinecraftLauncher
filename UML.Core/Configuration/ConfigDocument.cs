// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;

namespace UML.Core.Configuration;

public sealed class ConfigDocument
{
    public int Version { get; set; }
    public Dictionary<string, JsonElement> Values { get; } = new(StringComparer.Ordinal);
}
