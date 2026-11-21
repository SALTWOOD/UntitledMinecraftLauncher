// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Configuration;

internal static partial class ConfigSchema
{
    public static void Validate(IEnumerable<IConfigKey> keys)
    {
        var seen = new HashSet<(ConfigLayer Layer, string Name)>();
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key.Name))
                throw new ConfigException("Config key name cannot be empty.");
            if (!seen.Add((key.Layer, key.Name)))
                throw new ConfigException($"Duplicate config key '{key.Name}' in layer {key.Layer}.");
        }
    }
}
