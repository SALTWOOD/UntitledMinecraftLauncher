// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Configuration;

public sealed class ConfigKey<T> : IConfigKey
{
    public ConfigKey(string name, T defaultValue, ConfigLayer layer = ConfigLayer.Local)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        DefaultValue = defaultValue;
        Layer = layer;
    }

    public string Name { get; }
    public ConfigLayer Layer { get; }
    public T DefaultValue { get; }
    public bool IsSecret { get; init; }
    public Type ValueType => typeof(T);
    object? IConfigKey.DefaultValueObject => DefaultValue;
}
