// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Configuration;

public interface IConfigService
{
    T Get<T>(ConfigKey<T> key, ConfigContext context = default);
    void Set<T>(ConfigKey<T> key, T value, ConfigContext context = default);
    void Reset<T>(ConfigKey<T> key, ConfigContext context = default);
    bool IsDefault<T>(ConfigKey<T> key, ConfigContext context = default);
    IDisposable Watch<T>(ConfigKey<T> key, Action<ConfigChange<T>> handler, ConfigContext context = default);
    ValueTask InitializeAsync(CancellationToken cancellationToken = default);
    ValueTask FlushAsync(CancellationToken cancellationToken = default);
    event EventHandler<ConfigChangedEventArgs>? Changed;
    event EventHandler<ConfigSaveFailedEventArgs>? SaveFailed;
    event EventHandler<ConfigLoadFailedEventArgs>? LoadFailed;
}
