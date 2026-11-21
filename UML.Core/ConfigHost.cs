// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using UML.Core.Configuration;
using UML.Core.Configuration.Stores;

namespace UML.Core;

public static class ConfigHost
{
    public static ConfigService? CurrentService => ConfigService.Current as ConfigService;

    public static async ValueTask<ConfigService> InitializeAsync(
        string globalPath,
        string localPath,
        Func<string, string>? instancePathFactory = null,
        TimeSpan? saveDebounce = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(globalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);

        if (ConfigService.Current is ConfigService existing)
            await existing.DisposeAsync().ConfigureAwait(false);

        var builder = new ConfigBuilder()
            .UseGlobal(new JsonConfigStore(globalPath))
            .UseLocal(new JsonConfigStore(localPath));

        if (instancePathFactory is not null)
            builder.UseInstance(id => new JsonConfigStore(instancePathFactory(id)));

        if (saveDebounce is { } debounce)
            builder.UseSaveDebounce(debounce);

        var service = builder.Build();
        await service.InitializeAsync(cancellationToken).ConfigureAwait(false);
        ConfigService.Current = service;
        return service;
    }

    public static async ValueTask ShutdownAsync()
    {
        if (ConfigService.Current is ConfigService service)
            await service.DisposeAsync().ConfigureAwait(false);
    }
}
