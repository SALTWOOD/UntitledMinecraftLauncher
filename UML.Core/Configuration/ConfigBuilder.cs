// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Configuration;

public sealed class ConfigBuilder
{
    private readonly Dictionary<ConfigLayer, List<ConfigMigration>> _migrations = [];
    private IConfigStore? _global;
    private IConfigStore? _local;
    private Func<string, IConfigStore>? _instance;
    private TimeSpan _debounce = TimeSpan.FromMilliseconds(400);

    public ConfigBuilder UseGlobal(IConfigStore store)
    {
        _global = store ?? throw new ArgumentNullException(nameof(store));
        return this;
    }

    public ConfigBuilder UseLocal(IConfigStore store)
    {
        _local = store ?? throw new ArgumentNullException(nameof(store));
        return this;
    }

    public ConfigBuilder UseInstance(Func<string, IConfigStore> factory)
    {
        _instance = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    public ConfigBuilder UseSaveDebounce(TimeSpan debounce)
    {
        if (debounce < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(debounce));
        _debounce = debounce;
        return this;
    }

    public ConfigBuilder UseMigrations(ConfigLayer layer, params ConfigMigration[] migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);

        if (!_migrations.TryGetValue(layer, out var list))
            _migrations[layer] = list = [];
        list.AddRange(migrations);
        return this;
    }

    public ConfigService Build()
    {
        if (_global is null) throw new ConfigException("Global config store is not configured.");
        if (_local is null) throw new ConfigException("Local config store is not configured.");

        ConfigSchema.Validate(ConfigSchema.All);

        return new ConfigService(
            _global,
            _local,
            _instance,
            _debounce,
            _migrations.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<ConfigMigration>)pair.Value));
    }
}
