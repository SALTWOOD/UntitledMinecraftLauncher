// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Configuration;

[AttributeUsage(AttributeTargets.Property)]
public sealed class ConfigItemAttribute<TValue>(string key, TValue? defaultValue, ConfigLayer layer = ConfigLayer.Local) : Attribute
{
    public string Key { get; } = key;
    public TValue? DefaultValue { get; } = defaultValue;
    public ConfigLayer Layer { get; } = layer;
    public string? Description { get; set; }
}
