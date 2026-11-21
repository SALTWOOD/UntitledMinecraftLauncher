// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Profile;

public sealed record class TokenBundle(
    string MsAccessToken,
    string RefreshToken,
    DateTimeOffset MsExpiresAt,
    string XstsToken,
    string UserHash,
    string MinecraftAccessToken,
    DateTimeOffset MinecraftExpiresAt
);

public sealed record class MicrosoftProfile(
    string Id,
    string Username,
    Guid Uuid,
    string UserType,
    TokenBundle Tokens,
    string? SkinUrl = null,
    string? SkinVariant = null,
    DateTimeOffset? LastUsedAt = null
) : UserProfile;