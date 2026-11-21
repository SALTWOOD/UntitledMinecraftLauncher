// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Serialization;

namespace UML.Core.Authentication.Microsoft.Models;

public sealed record XboxAuthenticateRequest<T>
{
    [JsonPropertyName("Properties")] public required T Properties { get; init; }
    [JsonPropertyName("RelyingParty")] public required string RelyingParty { get; init; }
    [JsonPropertyName("TokenType")] public required string TokenType { get; init; }
}

public sealed record XboxUserProperties
{
    [JsonPropertyName("AuthMethod")] public string AuthMethod { get; init; } = "RPS";
    [JsonPropertyName("SiteName")] public string SiteName { get; init; } = "user.auth.xboxlive.com";
    [JsonPropertyName("RpsTicket")] public required string RpsTicket { get; init; }
}

public sealed record XstsProperties
{
    [JsonPropertyName("SandboxId")] public string SandboxId { get; init; } = "RETAIL";
    [JsonPropertyName("UserTokens")] public required List<string> UserTokens { get; init; }
}

public sealed record XboxTokenResponse
{
    [JsonPropertyName("IssueInstant")] public DateTimeOffset? IssueInstant { get; init; }
    [JsonPropertyName("NotAfter")] public DateTimeOffset? NotAfter { get; init; }
    [JsonPropertyName("Token")] public string? Token { get; init; }
    [JsonPropertyName("DisplayClaims")] public XboxDisplayClaims? DisplayClaims { get; init; }
}

public sealed record XboxDisplayClaims
{
    [JsonPropertyName("xui")] public List<XboxUserClaim>? Xui { get; init; }
}

public sealed record XboxUserClaim
{
    [JsonPropertyName("uhs")] public string? UserHash { get; init; }
}
