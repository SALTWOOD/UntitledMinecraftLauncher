// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Configuration;

public sealed class ConfigLoadFailedEventArgs(ConfigLayer layer, string? instanceId, Exception exception) : EventArgs
{
    public ConfigLayer Layer { get; } = layer;
    public string? InstanceId { get; } = instanceId;
    public Exception Exception { get; } = exception;
}
