// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Net;

namespace UML.Core.Authentication.Microsoft;

public sealed record MicrosoftLoginOptions
{
    public const string DefaultAuthority = "https://login.microsoftonline.com/consumers/oauth2/v2.0/";
    public const string DefaultScope = "XboxLive.signin offline_access";

    public string? ClientId { get; init; }
    public Uri Authority { get; init; } = new(DefaultAuthority);
    public string Scope { get; init; } = DefaultScope;
    public string? UserAgent { get; init; }
    public IWebProxy? Proxy { get; init; }
    public TimeSpan? RequestTimeout { get; init; }
}
