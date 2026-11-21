// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;

namespace UML.Core.Configuration.Stores;

public sealed class JsonConfigStore(string path) : IConfigStore
{
    private static readonly JsonSerializerOptions Options = new(ConfigJson.Options)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path = Path.GetFullPath(path);

    public string FilePath => _path;

    public async ValueTask<ConfigDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var primary = await TryLoadAsync(_path, cancellationToken).ConfigureAwait(false);
            var document = primary
                           ?? await TryLoadAsync(_path + ".bak", cancellationToken).ConfigureAwait(false);

            if (primary is null && File.Exists(_path))
            {
                try
                {
                    File.Move(_path, $"{_path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}", true);
                }
                catch
                {
                }
            }

            return document ?? new ConfigDocument();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask SaveAsync(ConfigDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var bytes = JsonSerializer.SerializeToUtf8Bytes(new DocumentDto
            {
                Version = document.Version,
                Values = document.Values
            }, Options);

            var temp = _path + ".tmp";
            try
            {
                await using (var stream = new FileStream(
                                 temp,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.None,
                                 4096,
                                 FileOptions.Asynchronous))
                {
                    await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(true);
                }

                if (File.Exists(_path))
                {
                    try
                    {
                        File.Copy(_path, _path + ".bak", true);
                    }
                    catch
                    {
                    }
                }

                File.Move(temp, _path, true);
            }
            catch
            {
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                catch
                {
                }

                throw;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async ValueTask<ConfigDocument?> TryLoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;

        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            if (bytes.Length == 0) return null;

            var dto = JsonSerializer.Deserialize<DocumentDto>(bytes, Options);
            if (dto is null) return null;

            var document = new ConfigDocument { Version = dto.Version };
            if (dto.Values is not null)
                foreach (var pair in dto.Values)
                    document.Values[pair.Key] = pair.Value;
            return document;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed class DocumentDto
    {
        public int Version { get; set; }
        public Dictionary<string, JsonElement>? Values { get; set; }
    }
}
