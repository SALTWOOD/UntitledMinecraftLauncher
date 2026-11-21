// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Configuration.Stores;

public sealed class MemoryConfigStore : IConfigStore
{
    private readonly object _gate = new();
    private ConfigDocument _document;

    public MemoryConfigStore() : this(new ConfigDocument())
    {
    }

    public MemoryConfigStore(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = Clone(document);
    }

    public int SaveCount { get; private set; }
    public Func<ConfigDocument, Exception?>? SaveFailure { get; set; }

    public ValueTask<ConfigDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return new ValueTask<ConfigDocument>(Clone(_document));
    }

    public ValueTask SaveAsync(ConfigDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        var failure = SaveFailure?.Invoke(document);
        if (failure is not null) throw failure;

        lock (_gate)
        {
            _document = Clone(document);
            SaveCount++;
        }

        return ValueTask.CompletedTask;
    }

    public ConfigDocument Snapshot()
    {
        lock (_gate)
            return Clone(_document);
    }

    private static ConfigDocument Clone(ConfigDocument document)
    {
        var clone = new ConfigDocument { Version = document.Version };
        foreach (var pair in document.Values)
            clone.Values[pair.Key] = pair.Value;
        return clone;
    }
}
