// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using UML.Core.App;
using UML.Core.Authentication.Microsoft.Models;
using UML.Core.Net;
using UML.Core.Profile;

namespace UML.Core.Authentication.Microsoft;

public delegate Task<MicrosoftToken?> DeviceCodeHandler(
    DeviceCodeData code,
    Func<CancellationToken, Task<MicrosoftToken>> poll,
    CancellationToken cancellationToken);

public sealed class MicrosoftLogin
{
    public const string HttpClientName = "microsoft";
    public const string DeviceCodePath = "devicecode";
    public const string TokenPath = "token";
    public const string DefaultUserType = "msa";

    private const string XboxLiveEndpoint = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsEndpoint = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string MinecraftLoginEndpoint = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string EntitlementsEndpoint = "https://api.minecraftservices.com/entitlements/mcstore";
    private const string MinecraftProfileEndpoint = "https://api.minecraftservices.com/minecraft/profile";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MicrosoftLogin(MicrosoftLoginOptions options, HttpClientPool? pool = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Authority.IsAbsoluteUri)
            throw new ArgumentException("Authority must be absolute URI.", nameof(options));

        var clientId = options.ClientId ?? Secrets.MicrosoftClientId;
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException(
                "Microsoft OAuth client id is not configured. " +
                "Set MicrosoftLoginOptions.ClientId or provide UML_MS_CLIENT_ID.");

        Options = options;
        ClientId = clientId.Trim();
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

    public string ClientId { get; }

    public HttpClient Client { get; }

    public async Task<DeviceCodeData> RequestDeviceCodeAsync(CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = Options.Scope
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, DeviceCodePath)
        {
            Content = content
        };
        using var response = await Client
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        var data = await DeserializeAsync<DeviceCodeData>(response, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(data.Error))
            throw new AuthenticationException(data.Error, data.ErrorDescription ?? "Device code request failed.");
        if (string.IsNullOrWhiteSpace(data.DeviceCode) || string.IsNullOrWhiteSpace(data.UserCode))
            throw new AuthenticationException("invalid_response", "Device code response is missing required fields.");

        return data;
    }

    public async Task<MicrosoftToken> PollDeviceCodeAsync(
        DeviceCodeData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            ["device_code"] = data.DeviceCode ?? string.Empty
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenPath)
        {
            Content = content
        };
        using var response = await Client
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return await DeserializeAsync<MicrosoftToken>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MicrosoftToken> WaitForDeviceCodeAsync(
        DeviceCodeData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        var interval = TimeSpan.FromSeconds(data.Interval is > 0 ? data.Interval.Value : 5);

        while (true)
        {
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);

            var token = await PollDeviceCodeAsync(data, cancellationToken).ConfigureAwait(false);
            if (!token.IsError) return token;

            switch (token.Error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += TimeSpan.FromSeconds(5);
                    continue;
                case "expired_token":
                    throw new AuthenticationException(token.Error, "Device code has expired.");
                case "access_denied":
                case "authorization_declined":
                    throw new AuthenticationException(token.Error, "The user declined the device code authorization.");
                default:
                    throw new AuthenticationException(
                        token.Error ?? "invalid_response",
                        token.ErrorDescription ?? "Unknown device code error.");
            }
        }
    }

    public async Task<MicrosoftToken> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = Options.Scope
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenPath)
        {
            Content = content
        };
        using var response = await Client
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        var token = await DeserializeAsync<MicrosoftToken>(response, cancellationToken).ConfigureAwait(false);
        EnsureTokenSuccess(token);
        return token;
    }

    public async Task<MicrosoftAccount> AuthenticateWithDeviceCodeAsync(
        Action<DeviceCodeData>? onCode,
        CancellationToken cancellationToken = default)
    {
        var code = await RequestDeviceCodeAsync(cancellationToken).ConfigureAwait(false);
        onCode?.Invoke(code);

        var token = await WaitForDeviceCodeAsync(code, cancellationToken).ConfigureAwait(false);
        return await CompleteAsync(token, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MicrosoftAccount> LoginAsync(
        DeviceCodeHandler handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var code = await RequestDeviceCodeAsync(cancellationToken).ConfigureAwait(false);
        var token = await handler(
                code,
                pollToken => PollDeviceCodeAsync(code, pollToken),
                cancellationToken)
            .ConfigureAwait(false);

        if (token is null)
            throw new OperationCanceledException("Device code login was cancelled.");

        EnsureTokenSuccess(token);
        return await CompleteAsync(token, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MicrosoftAccount> LoginWithRefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var token = await RefreshAsync(refreshToken, cancellationToken).ConfigureAwait(false);
        token = token with { RefreshToken = token.RefreshToken ?? refreshToken };

        return await CompleteAsync(token, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MicrosoftAccount> CompleteAsync(
        MicrosoftToken token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        EnsureTokenSuccess(token);

        var msExpiresAt = token.ExpiresIn is > 0
            ? DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn.Value)
            : DateTimeOffset.UtcNow.AddHours(1);

        var xbl = await AuthenticateXboxAsync(token.AccessToken!, cancellationToken).ConfigureAwait(false);
        var xsts = await AuthorizeXstsAsync(xbl.Token!, cancellationToken).ConfigureAwait(false);

        var userHash = xsts.DisplayClaims?.Xui?.FirstOrDefault()?.UserHash;
        if (string.IsNullOrWhiteSpace(userHash))
            throw new AuthenticationException("xsts_missing_user_hash", "XSTS response did not contain a user hash.");

        var minecraft = await AuthenticateMinecraftAsync(xsts.Token!, userHash, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(minecraft.AccessToken))
            throw new AuthenticationException("minecraft_authentication_failed", "Minecraft did not return an access token.");

        await EnsureOwnsMinecraftAsync(minecraft.AccessToken, cancellationToken).ConfigureAwait(false);
        var profile = await GetMinecraftProfileAsync(minecraft.AccessToken, cancellationToken).ConfigureAwait(false);

        if (!Guid.TryParse(profile.Id, out var uuid))
            throw new AuthenticationException("invalid_profile", "Minecraft profile id is not a valid UUID.");

        var skin = profile.Skins?.FirstOrDefault(item =>
                       string.Equals(item.State, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                   ?? profile.Skins?.FirstOrDefault();

        return new MicrosoftAccount
        {
            AccountId = userHash,
            Username = profile.Name,
            Uuid = uuid,
            UserType = DefaultUserType,
            Tokens = new TokenBundle(
                token.AccessToken!,
                token.RefreshToken ?? string.Empty,
                msExpiresAt,
                xsts.Token!,
                userHash,
                minecraft.AccessToken,
                minecraft.ExpiresIn > 0
                    ? DateTimeOffset.UtcNow.AddSeconds(minecraft.ExpiresIn)
                    : DateTimeOffset.UtcNow.AddHours(24)),
            SkinUrl = skin?.Url,
            SkinVariant = skin?.Variant
        };
    }

    private Task<XboxTokenResponse> AuthenticateXboxAsync(
        string msAccessToken,
        CancellationToken cancellationToken) =>
        PostXboxAsync(
            XboxLiveEndpoint,
            new XboxAuthenticateRequest<XboxUserProperties>
            {
                Properties = new XboxUserProperties { RpsTicket = $"d={msAccessToken}" },
                RelyingParty = "http://auth.xboxlive.com",
                TokenType = "JWT"
            },
            cancellationToken);

    private Task<XboxTokenResponse> AuthorizeXstsAsync(
        string xboxToken,
        CancellationToken cancellationToken) =>
        PostXboxAsync(
            XstsEndpoint,
            new XboxAuthenticateRequest<XstsProperties>
            {
                Properties = new XstsProperties { UserTokens = [xboxToken] },
                RelyingParty = "rp://api.minecraftservices.com/",
                TokenType = "JWT"
            },
            cancellationToken);

    private async Task<XboxTokenResponse> PostXboxAsync<T>(
        string endpoint,
        XboxAuthenticateRequest<T> payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        using var response = await Client
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateXboxError(response.StatusCode, body);

        var token = JsonSerializer.Deserialize<XboxTokenResponse>(body, JsonOptions);
        if (token is null || string.IsNullOrWhiteSpace(token.Token))
            throw new AuthenticationException("invalid_response", "Xbox response did not contain a token.");

        return token;
    }

    private async Task<MinecraftTokenResponse> AuthenticateMinecraftAsync(
        string xstsToken,
        string userHash,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, MinecraftLoginEndpoint)
        {
            Content = JsonContent.Create(
                new MinecraftLoginRequest { IdentityToken = $"XBL3.0 x={userHash};{xstsToken}" },
                options: JsonOptions)
        };
        using var response = await Client
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, "minecraft_authentication_failed", cancellationToken)
            .ConfigureAwait(false);

        return await DeserializeAsync<MinecraftTokenResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureOwnsMinecraftAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, EntitlementsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await Client
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, "minecraft_entitlements_failed", cancellationToken)
            .ConfigureAwait(false);

        var entitlements = await DeserializeAsync<MinecraftEntitlements>(response, cancellationToken)
            .ConfigureAwait(false);

        var owned = entitlements.Items?.Any(item =>
            item.Name is "product_minecraft" or "game_minecraft") ?? false;

        if (!owned)
            throw new AuthenticationException(
                "minecraft_not_owned",
                "The Microsoft account does not own Minecraft.");
    }

    private async Task<MinecraftProfileResponse> GetMinecraftProfileAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MinecraftProfileEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await Client
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new AuthenticationException(
                "profile_not_created",
                "The Microsoft account has not created a Minecraft Java profile yet.");

        await EnsureSuccessAsync(response, "minecraft_profile_failed", cancellationToken)
            .ConfigureAwait(false);

        return await DeserializeAsync<MinecraftProfileResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureTokenSuccess(MicrosoftToken token)
    {
        if (token.IsError)
            throw new AuthenticationException(
                token.Error!,
                token.ErrorDescription ?? "Microsoft OAuth returned an error.");

        if (string.IsNullOrWhiteSpace(token.AccessToken))
            throw new AuthenticationException("invalid_token", "Microsoft OAuth did not return an access token.");
    }

    private static async Task<T> DeserializeAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                   ?? throw new AuthenticationException(
                       "invalid_response",
                       $"Response body was empty (HTTP {(int)response.StatusCode}).");
        }
        catch (JsonException exception)
        {
            throw new AuthenticationException(
                "invalid_response",
                $"Failed to parse response (HTTP {(int)response.StatusCode}).",
                exception);
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string error,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var detail = ExtractError(body);

        throw new AuthenticationException(
            error,
            $"Request failed (HTTP {(int)response.StatusCode})" +
            (string.IsNullOrWhiteSpace(detail) ? "." : $": {detail}"));
    }

    private static AuthenticationException CreateXboxError(HttpStatusCode status, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var message = root.TryGetProperty("Message", out var messageNode) &&
                          messageNode.ValueKind == JsonValueKind.String
                ? messageNode.GetString()
                : null;

            if (root.TryGetProperty("XErr", out var xErrNode) &&
                xErrNode.TryGetInt64(out var xErr))
                return new AuthenticationException(
                    $"xsts_{xErr.ToString(CultureInfo.InvariantCulture)}",
                    message ?? $"XSTS authorization failed with XErr {xErr}.");

            return new AuthenticationException(
                "xbox_authentication_failed",
                message ?? $"Xbox service returned HTTP {(int)status}.");
        }
        catch (JsonException)
        {
            return new AuthenticationException(
                "xbox_authentication_failed",
                $"Xbox service returned HTTP {(int)status}.");
        }
    }

    private static string? ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            foreach (var name in new[] { "errorMessage", "error_description", "error", "Message", "message" })
            {
                if (document.RootElement.TryGetProperty(name, out var node) &&
                    node.ValueKind == JsonValueKind.String)
                    return node.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static Uri NormalizeAuthority(Uri authority)
    {
        var value = authority.AbsoluteUri;
        return value.EndsWith('/') ? authority : new Uri(value + "/");
    }
}
