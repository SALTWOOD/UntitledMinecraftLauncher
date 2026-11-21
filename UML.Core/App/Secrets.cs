// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.App;

public static partial class Secrets
{
    public static string? MicrosoftClientId { get; } = Get("MS_CLIENT_ID");

    public static string? Get(
        string key,
        bool allowEnvironment = true,
        bool environmentDebugOnly = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (CreateEmbeddedSecrets().TryGetValue(key, out var embedded) &&
            !string.IsNullOrWhiteSpace(embedded))
            return embedded.Trim();

        if (!allowEnvironment) return null;

#if !DEBUG
        if (environmentDebugOnly) return null;
#endif

        var value = Environment.GetEnvironmentVariable($"UML_{key}");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
