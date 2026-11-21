// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Concurrent;

namespace UML.Core.Configuration;

public sealed class ConfigService : IConfigService, IAsyncDisposable
{
    private static IConfigService _current = UninitializedConfigService.Instance;

    private readonly ConfigLayerState _global;
    private readonly ConfigLayerState _local;
    private readonly Func<string, IConfigStore>? _instanceFactory;
    private readonly IReadOnlyDictionary<ConfigLayer, IReadOnlyList<ConfigMigration>>? _migrations;
    private readonly ConcurrentDictionary<string, ConfigLayerState> _instances = new(StringComparer.Ordinal);
    private readonly TimeSpan _debounce;

    public ConfigService(
        IConfigStore globalStore,
        IConfigStore localStore,
        Func<string, IConfigStore>? instanceStoreFactory = null,
        TimeSpan? saveDebounce = null,
        IReadOnlyDictionary<ConfigLayer, IReadOnlyList<ConfigMigration>>? migrations = null)
    {
        ArgumentNullException.ThrowIfNull(globalStore);
        ArgumentNullException.ThrowIfNull(localStore);

        _debounce = saveDebounce ?? TimeSpan.FromMilliseconds(400);
        if (_debounce < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(saveDebounce));

        _instanceFactory = instanceStoreFactory;
        _migrations = migrations;
        _global = CreateState(ConfigLayer.Global, null, globalStore);
        _local = CreateState(ConfigLayer.Local, null, localStore);
    }

    public static IConfigService Current
    {
        get => Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, value ?? throw new ArgumentNullException(nameof(value)));
    }

    public event EventHandler<ConfigChangedEventArgs>? Changed;
    public event EventHandler<ConfigSaveFailedEventArgs>? SaveFailed;
    public event EventHandler<ConfigLoadFailedEventArgs>? LoadFailed;

    public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _global.EnsureLoaded();
        _local.EnsureLoaded();
        return ValueTask.CompletedTask;
    }

    public T Get<T>(ConfigKey<T> key, ConfigContext context = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Resolve(key, context).Get(key);
    }

    public void Set<T>(ConfigKey<T> key, T value, ConfigContext context = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var state = Resolve(key, context);
        if (!state.Set(key, value, out var oldValue, out var newValue)) return;

        state.Notify(key, oldValue, newValue);
        Changed?.Invoke(this, new ConfigChangedEventArgs(key, context, oldValue, newValue));
    }

    public void Reset<T>(ConfigKey<T> key, ConfigContext context = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var state = Resolve(key, context);
        if (!state.Reset(key, out var oldValue, out var newValue)) return;

        state.Notify(key, oldValue, newValue);
        Changed?.Invoke(this, new ConfigChangedEventArgs(key, context, oldValue, newValue));
    }

    public bool IsDefault<T>(ConfigKey<T> key, ConfigContext context = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Resolve(key, context).IsDefault(key);
    }

    public IDisposable Watch<T>(ConfigKey<T> key, Action<ConfigChange<T>> handler, ConfigContext context = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(handler);
        return Resolve(key, context).Watch(key, handler, context);
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        await _global.FlushAsync(cancellationToken).ConfigureAwait(false);
        await _local.FlushAsync(cancellationToken).ConfigureAwait(false);

        foreach (var state in _instances.Values)
            await state.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _global.DisposeAsync().ConfigureAwait(false);
        await _local.DisposeAsync().ConfigureAwait(false);

        foreach (var state in _instances.Values)
            await state.DisposeAsync().ConfigureAwait(false);

        if (ReferenceEquals(Current, this))
            Current = UninitializedConfigService.Instance;
    }

    private ConfigLayerState CreateState(ConfigLayer layer, string? instanceId, IConfigStore store) =>
        new(
            store,
            _debounce,
            _migrations is not null && _migrations.TryGetValue(layer, out var migrations)
                ? migrations
                : Array.Empty<ConfigMigration>(),
            exception => SaveFailed?.Invoke(this, new ConfigSaveFailedEventArgs(layer, instanceId, exception)),
            exception => LoadFailed?.Invoke(this, new ConfigLoadFailedEventArgs(layer, instanceId, exception)));

    private ConfigLayerState Resolve(IConfigKey key, ConfigContext context) => key.Layer switch
    {
        ConfigLayer.Global => _global,
        ConfigLayer.Local => _local,
        ConfigLayer.Instance => ResolveInstance(context),
        _ => throw new ConfigScopeException($"Unknown config layer '{key.Layer}'.")
    };

    private ConfigLayerState ResolveInstance(ConfigContext context)
    {
        var factory = _instanceFactory
                      ?? throw new ConfigScopeException("Instance config store is not configured.");

        var instanceId = context.InstanceId;
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ConfigScopeException("Instance config requires a non-empty InstanceId.");

        instanceId = instanceId.Trim();

        return _instances.GetOrAdd(instanceId, id =>
        {
            var store = factory(id)
                        ?? throw new ConfigScopeException("Instance store factory returned null.");
            return CreateState(ConfigLayer.Instance, id, store);
        });
    }

    private sealed class UninitializedConfigService : IConfigService
    {
        public static readonly UninitializedConfigService Instance = new();

        public event EventHandler<ConfigChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public event EventHandler<ConfigSaveFailedEventArgs>? SaveFailed
        {
            add { }
            remove { }
        }

        public event EventHandler<ConfigLoadFailedEventArgs>? LoadFailed
        {
            add { }
            remove { }
        }

        public T Get<T>(ConfigKey<T> key, ConfigContext context = default) => throw Error();

        public void Set<T>(ConfigKey<T> key, T value, ConfigContext context = default) => throw Error();

        public void Reset<T>(ConfigKey<T> key, ConfigContext context = default) => throw Error();

        public bool IsDefault<T>(ConfigKey<T> key, ConfigContext context = default) => throw Error();

        public IDisposable Watch<T>(ConfigKey<T> key, Action<ConfigChange<T>> handler, ConfigContext context = default) =>
            throw Error();

        public ValueTask InitializeAsync(CancellationToken cancellationToken = default) => throw Error();

        public ValueTask FlushAsync(CancellationToken cancellationToken = default) => throw Error();

        private static ConfigException Error() => new("ConfigService.Current has not been initialized.");
    }
}
