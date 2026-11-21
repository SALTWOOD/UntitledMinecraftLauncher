// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Serialization;

namespace UML.Core.Authentication.Microsoft.Models;

public sealed record MinecraftLoginRequest
{
    [JsonPropertyName("identityToken")] public required string IdentityToken { get; init; }
}

public sealed record MinecraftTokenResponse
{
    [JsonPropertyName("username")] public string? Username { get; init; }
    [JsonPropertyName("access_token")] public string? AccessToken { get; init; }
    [JsonPropertyName("token_type")] public string? TokenType { get; init; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }
}

public sealed record MinecraftEntitlements
{
    [JsonPropertyName("items")] public List<MinecraftEntitlement>? Items { get; init; }
}

public sealed record MinecraftEntitlement
{
    [JsonPropertyName("name")] public string? Name { get; init; }
}

public sealed record MinecraftProfileResponse
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("skins")] public List<MinecraftSkin>? Skins { get; init; }
}

public sealed record MinecraftSkin
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("state")] public string? State { get; init; }
    [JsonPropertyName("url")] public string? Url { get; init; }
    [JsonPropertyName("variant")] public string? Variant { get; init; }
}
