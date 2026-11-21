// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using UML.Core.Profile;

namespace UML.Core.Authentication.Microsoft;

public sealed record MicrosoftAccount
{
    public required string AccountId { get; init; }
    public required string Username { get; init; }
    public required Guid Uuid { get; init; }
    public required string UserType { get; init; }
    public required TokenBundle Tokens { get; init; }
    public string? SkinUrl { get; init; }
    public string? SkinVariant { get; init; }
}
