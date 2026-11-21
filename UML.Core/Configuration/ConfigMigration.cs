// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Configuration;

public sealed class ConfigMigration
{
    public required int FromVersion { get; init; }
    public required Action<ConfigDocument> Apply { get; init; }
}
