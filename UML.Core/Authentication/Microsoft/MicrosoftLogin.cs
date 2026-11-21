// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using UML.Core.Net;

namespace UML.Core.Authentication.Microsoft;

public sealed class MicrosoftLogin
{
    public const string HttpClientName = "microsoft";
    public const string DeviceCodePath = "devicecode";
    public const string TokenPath = "token";

    public MicrosoftLogin(MicrosoftLoginOptions options, HttpClientPool? pool = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientId);
        if (!options.Authority.IsAbsoluteUri)
            throw new ArgumentException("Authority must be absolute URI.", nameof(options));

        Options = options;
        Client = (pool ?? HttpClientPool.Shared).Get(HttpClientName, new HttpClientProfile
        {
            BaseAddress = NormalizeAuthority(options.Authority),
            Proxy = options.Proxy,
            UserAgent = options.UserAgent ?? HttpClientPool.DefaultUserAgent,
            RequestTimeout = options.RequestTimeout,
            DefaultHeaders = new Dictionary<string, string>
            {
                ["Accept"] = "application/json"
            }
        });
    }

    public MicrosoftLoginOptions Options { get; }

    public HttpClient Client { get; }

    private static Uri NormalizeAuthority(Uri authority)
    {
        var value = authority.AbsoluteUri;
        return value.EndsWith('/') ? authority : new Uri(value + "/");
    }
}
