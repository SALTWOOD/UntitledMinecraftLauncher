// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Configuration;

public sealed class ConfigChangedEventArgs(IConfigKey key, ConfigContext context, object? oldValue, object? newValue) : EventArgs
{
    public IConfigKey Key { get; } = key;
    public ConfigContext Context { get; } = context;
    public object? OldValue { get; } = oldValue;
    public object? NewValue { get; } = newValue;
}
