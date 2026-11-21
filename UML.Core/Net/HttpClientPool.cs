// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Concurrent;
using System.Net;

namespace UML.Core.Net;

public sealed record HttpClientProfile
{
    public Uri? BaseAddress { get; init; }
    public string? UserAgent { get; init; } = HttpClientPool.DefaultUserAgent;
    public IWebProxy? Proxy { get; init; }
    public bool UseProxy { get; init; } = true;
    public bool UseCookies { get; init; }
    public bool AllowAutoRedirect { get; init; } = true;
    public int MaxAutomaticRedirections { get; init; } = 20;
    public TimeSpan PooledConnectionLifetime { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan PooledConnectionIdleTimeout { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan ConnectTimeout { get; init; } = Timeout.InfiniteTimeSpan;
    public TimeSpan? RequestTimeout { get; init; }
    public DecompressionMethods AutomaticDecompression { get; init; } = DecompressionMethods.All;
    public Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>>? ConnectCallback { get; init; }
    public IReadOnlyDictionary<string, string> DefaultHeaders { get; init; } = new Dictionary<string, string>();
    public Action<SocketsHttpHandler>? ConfigureHandler { get; init; }
    public Action<HttpClient>? ConfigureClient { get; init; }
}

public sealed class HttpClientPool : IDisposable
{
    public const string DefaultName = "default";

    public static HttpClientPool Shared { get; } = new();

    public static string DefaultUserAgent { get; } = CreateDefaultUserAgent();

    private readonly ConcurrentDictionary<string, Lazy<ClientEntry>> _clients = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, HttpClientProfile> _profiles = new(StringComparer.Ordinal);
    private readonly HttpClientProfile _defaultProfile;
    private int _disposed;

    public HttpClientPool(HttpClientProfile? defaultProfile = null)
    {
        _defaultProfile = defaultProfile ?? new HttpClientProfile();
    }

    public HttpClientPool Configure(string name, HttpClientProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(profile);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (_clients.ContainsKey(name))
            throw new InvalidOperationException($"HttpClient「{name}」已创建，无法再修改配置。");

        _profiles[name] = profile;
        return this;
    }

    public HttpClient Get(string name = DefaultName)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return GetOrCreate(name, null).Client;
    }

    public HttpClient Get(string name, HttpClientProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return GetOrCreate(name, profile).Client;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        foreach (var entry in _clients.Values)
            if (entry.IsValueCreated)
                entry.Value.Client.Dispose();

        _clients.Clear();
        _profiles.Clear();
    }

    private ClientEntry GetOrCreate(string name, HttpClientProfile? inlineProfile)
    {
        if (inlineProfile is not null)
            _profiles.TryAdd(name, inlineProfile);

        var entry = _clients.GetOrAdd(
            name,
            static (key, state) => new Lazy<ClientEntry>(
                () => state.Pool.CreateEntry(key, state.Profile),
                LazyThreadSafetyMode.ExecutionAndPublication),
            (Pool: this, Profile: inlineProfile));

        return entry.Value;
    }

    private ClientEntry CreateEntry(string name, HttpClientProfile? inlineProfile)
    {
        var profile = inlineProfile
                      ?? (_profiles.TryGetValue(name, out var configured) ? configured : _defaultProfile);

        var handler = new SocketsHttpHandler
        {
            UseProxy = profile.UseProxy,
            Proxy = profile.Proxy,
            UseCookies = profile.UseCookies,
            AllowAutoRedirect = profile.AllowAutoRedirect,
            MaxAutomaticRedirections = profile.MaxAutomaticRedirections,
            AutomaticDecompression = profile.AutomaticDecompression,
            EnableMultipleHttp2Connections = true,
            PooledConnectionLifetime = profile.PooledConnectionLifetime,
            PooledConnectionIdleTimeout = profile.PooledConnectionIdleTimeout,
            ConnectTimeout = profile.ConnectTimeout
        };

        if (profile.ConnectCallback is not null)
            handler.ConnectCallback = profile.ConnectCallback;

        profile.ConfigureHandler?.Invoke(handler);

        var client = new HttpClient(handler, disposeHandler: true);

        if (profile.RequestTimeout is { } timeout)
            client.Timeout = timeout;
        if (profile.BaseAddress is not null)
            client.BaseAddress = profile.BaseAddress;
        if (!string.IsNullOrWhiteSpace(profile.UserAgent))
            client.DefaultRequestHeaders.UserAgent.ParseAdd(profile.UserAgent);

        foreach (var (key, value) in profile.DefaultHeaders)
            client.DefaultRequestHeaders.TryAddWithoutValidation(key, value);

        profile.ConfigureClient?.Invoke(client);

        return new ClientEntry(client);
    }

    private static string CreateDefaultUserAgent()
    {
        var assembly = typeof(HttpClientPool).Assembly;
        var name = assembly.GetName().Name ?? "UML";
        var version = assembly.GetName().Version;
        return version is null ? name : $"{name}/{version.ToString(3)}";
    }

    private sealed record ClientEntry(HttpClient Client);
}
