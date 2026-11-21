// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Serialization;

namespace UML.Core.Authentication.Microsoft.Models;

public sealed record DeviceCodeData
{
    [JsonPropertyName("device_code")] public string? DeviceCode { get; init; }
    [JsonPropertyName("user_code")] public string? UserCode { get; init; }
    [JsonPropertyName("verification_uri")] public string? VerificationUri { get; init; }

    [JsonPropertyName("verification_uri_complete")]
    public string? VerificationUriComplete { get; init; }

    [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }
    [JsonPropertyName("interval")] public int? Interval { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
    [JsonPropertyName("error_description")] public string? ErrorDescription { get; init; }
}
