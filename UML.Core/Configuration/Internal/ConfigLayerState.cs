// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;

namespace UML.Core.Configuration;

internal sealed class ConfigLayerState
{
    private readonly IConfigStore _store;
    private readonly TimeSpan _debounce;
    private readonly IReadOnlyList<ConfigMigration> _migrations;
    private readonly Action<Exception> _onSaveFailed;
    private readonly Action<Exception> _onLoadFailed;
    private readonly object _gate = new();
    private readonly object _watchGate = new();
    private readonly object _timerGate = new();
    private readonly ConcurrentDictionary<IConfigKey, CacheEntry> _cache = new();
    private readonly Dictionary<IConfigKey, List<Action<object?, object?>>> _watchers = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private ImmutableDictionary<string, JsonElement> _snapshot =
        ImmutableDictionary.Create<string, JsonElement>(StringComparer.Ordinal);
    private Timer? _timer;
    private bool _dirty;
    private bool _loaded;
    private bool _disposed;
    private long _version;
    private int _documentVersion;

    public ConfigLayerState(
        IConfigStore store,
        TimeSpan debounce,
        IReadOnlyList<ConfigMigration> migrations,
        Action<Exception> onSaveFailed,
        Action<Exception> onLoadFailed)
    {
        _store = store;
        _debounce = debounce;
        _migrations = migrations.OrderBy(migration => migration.FromVersion).ToArray();
        _onSaveFailed = onSaveFailed;
        _onLoadFailed = onLoadFailed;
    }

    public void EnsureLoaded()
    {
        if (Volatile.Read(ref _loaded)) return;

        lock (_gate)
        {
            if (_loaded) return;

            var document = LoadDocument();
            var original = Clone(document);
            var dirtyBefore = _dirty;

            try
            {
                ApplyMigrations(document);
            }
            catch (Exception exception)
            {
                document = original;
                _dirty = dirtyBefore;
                _onLoadFailed(exception);
            }

            _snapshot = document.Values.ToImmutableDictionary(StringComparer.Ordinal);
            _documentVersion = document.Version;
            _version++;
            _cache.Clear();
            _loaded = true;
        }

        if (Volatile.Read(ref _dirty))
            ScheduleSave();
    }

    public T Get<T>(ConfigKey<T> key)
    {
        EnsureLoaded();

        var version = Volatile.Read(ref _version);
        if (_cache.TryGetValue(key, out var cached) && cached.Version == version)
            return (T)cached.Value!;

        JsonElement element;
        lock (_gate)
        {
            if (!_snapshot.TryGetValue(key.Name, out element))
                return key.DefaultValue;
            version = _version;
        }

        T value;
        try
        {
            value = JsonSerializer.Deserialize<T>(element, ConfigJson.Options)!;
        }
        catch (JsonException exception)
        {
            throw new ConfigException($"Failed to deserialize config '{key.Name}'.", exception);
        }

        _cache[key] = new CacheEntry(value, version);
        return value;
    }

    public bool Set<T>(ConfigKey<T> key, T value, out T oldValue, out T newValue)
    {
        oldValue = Get(key);
        newValue = value;

        if (EqualityComparer<T>.Default.Equals(oldValue, value))
            return false;

        JsonElement element;
        try
        {
            element = JsonSerializer.SerializeToElement(value, ConfigJson.Options);
        }
        catch (Exception exception)
        {
            throw new ConfigException($"Failed to serialize config '{key.Name}'.", exception);
        }

        lock (_gate)
        {
            _snapshot = _snapshot.SetItem(key.Name, element);
            _version++;
            _cache[key] = new CacheEntry(value, _version);
            _dirty = true;
        }

        ScheduleSave();
        return true;
    }

    public bool Reset<T>(ConfigKey<T> key, out T oldValue, out T newValue)
    {
        oldValue = Get(key);
        newValue = key.DefaultValue;

        lock (_gate)
        {
            if (!_snapshot.ContainsKey(key.Name))
                return false;

            _snapshot = _snapshot.Remove(key.Name);
            _version++;
            _cache.TryRemove(key, out _);
            _dirty = true;
        }

        ScheduleSave();
        return true;
    }

    public bool IsDefault<T>(ConfigKey<T> key)
    {
        EnsureLoaded();

        lock (_gate)
        {
            if (!_snapshot.ContainsKey(key.Name))
                return true;
        }

        return EqualityComparer<T>.Default.Equals(Get(key), key.DefaultValue);
    }

    public IDisposable Watch<T>(ConfigKey<T> key, Action<ConfigChange<T>> handler, ConfigContext context)
    {
        EnsureLoaded();

        Action<object?, object?> wrapper = (oldValue, newValue) =>
            handler(new ConfigChange<T>(key, (T)oldValue!, (T)newValue!, context));

        lock (_watchGate)
        {
            if (!_watchers.TryGetValue(key, out var handlers))
                _watchers[key] = handlers = [];
            handlers.Add(wrapper);
        }

        return new Subscription(() =>
        {
            lock (_watchGate)
            {
                if (!_watchers.TryGetValue(key, out var handlers)) return;
                handlers.Remove(wrapper);
                if (handlers.Count == 0)
                    _watchers.Remove(key);
            }
        });
    }

    public void Notify(IConfigKey key, object? oldValue, object? newValue)
    {
        Action<object?, object?>[] handlers;
        lock (_watchGate)
        {
            if (!_watchers.TryGetValue(key, out var list) || list.Count == 0)
                return;
            handlers = [.. list];
        }

        foreach (var handler in handlers)
        {
            try
            {
                handler(oldValue, newValue);
            }
            catch
            {
            }
        }
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        lock (_timerGate)
        {
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        lock (_timerGate)
        {
            _timer?.Dispose();
            _timer = null;
        }

        try
        {
            await FlushAsync().ConfigureAwait(false);
        }
        catch
        {
        }

        _disposed = true;
        _saveLock.Dispose();
    }

    private ConfigDocument LoadDocument()
    {
        try
        {
            return _store.LoadAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _onLoadFailed(exception);
            return new ConfigDocument();
        }
    }

    private void ApplyMigrations(ConfigDocument document)
    {
        if (_migrations.Count == 0) return;

        var changed = false;
        foreach (var migration in _migrations)
        {
            if (document.Version > migration.FromVersion) continue;
            migration.Apply(document);
            document.Version = migration.FromVersion + 1;
            changed = true;
        }

        if (changed)
            _dirty = true;
    }

    private void ScheduleSave()
    {
        if (_disposed) return;

        if (_debounce <= TimeSpan.Zero)
        {
            _ = SaveAsync();
            return;
        }

        lock (_timerGate)
        {
            _timer ??= new Timer(_ => _ = SaveAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            await SaveCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed) return;

            ConfigDocument document;
            lock (_gate)
            {
                if (!_dirty) return;
                _dirty = false;
                document = new ConfigDocument { Version = _documentVersion };
                foreach (var pair in _snapshot)
                    document.Values[pair.Key] = pair.Value;
            }

            try
            {
                await SaveWithRetryAsync(document, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lock (_gate) _dirty = true;
                _onSaveFailed(exception);
            }
            catch
            {
                lock (_gate) _dirty = true;
                throw;
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private async Task SaveWithRetryAsync(ConfigDocument document, CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                last = exception;
                if (attempt < 2)
                    await Task.Delay(
                            TimeSpan.FromMilliseconds(100 * Math.Pow(3, attempt)),
                            cancellationToken)
                        .ConfigureAwait(false);
            }
        }

        throw last!;
    }

    private static ConfigDocument Clone(ConfigDocument document)
    {
        var clone = new ConfigDocument { Version = document.Version };
        foreach (var pair in document.Values)
            clone.Values[pair.Key] = pair.Value;
        return clone;
    }

    private readonly record struct CacheEntry(object? Value, long Version);

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
