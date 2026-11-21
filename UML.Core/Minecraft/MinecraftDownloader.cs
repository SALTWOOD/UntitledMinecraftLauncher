// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using Downloader;
using UML.Core.Net;

namespace UML.Core.Minecraft;

public sealed record MinecraftDownloadItem(
    MinecraftResource Resource,
    string Path,
    long Size);

public sealed class MinecraftDownloader
{
    private const string HttpClientName = "minecraft";
    private const int MaxParallelDownloads = 8;
    private const long ChunkThreshold = 4L * 1024 * 1024;

    private readonly IMinecraftSource _source;
    private readonly HttpClient _client;

    public MinecraftDownloader(IMinecraftSource source, HttpClientPool? pool = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        _client = (pool ?? HttpClientPool.Shared).Get(HttpClientName, new HttpClientProfile
        {
            RequestTimeout = TimeSpan.FromMinutes(2)
        });
    }

    public IMinecraftSource Source => _source;

    public HttpClient Client => _client;

    public async Task<string> GetStringAsync(
        MinecraftResource resource,
        CancellationToken cancellationToken = default)
    {
        Exception? last = null;

        foreach (var url in _source.Resolve(resource).Distinct(StringComparer.Ordinal))
        {
            try
            {
                return await _client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                last = exception;
            }
        }

        throw new HttpRequestException($"Failed to fetch '{resource.Url}'.", last);
    }

    public async Task DownloadAsync(
        IEnumerable<MinecraftDownloadItem> items,
        string stage,
        IProgress<MinecraftInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var pending = new List<MinecraftDownloadItem>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (IsReady(item.Path, item.Size)) continue;
            if (!paths.Add(item.Path)) continue;
            pending.Add(item);
        }

        progress?.Report(new MinecraftInstallProgress(stage, 0, pending.Count));
        if (pending.Count == 0) return;

        var completed = 0;

        await Parallel.ForEachAsync(
                pending,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxParallelDownloads,
                    CancellationToken = cancellationToken
                },
                async (item, token) =>
                {
                    await DownloadOneAsync(item, token).ConfigureAwait(false);
                    progress?.Report(
                        new MinecraftInstallProgress(
                            stage,
                            Interlocked.Increment(ref completed),
                            pending.Count));
                })
            .ConfigureAwait(false);
    }

    private async Task DownloadOneAsync(
        MinecraftDownloadItem item,
        CancellationToken cancellationToken)
    {
        Exception? last = null;

        foreach (var url in _source.Resolve(item.Resource).Distinct(StringComparer.Ordinal))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.Path)!);

                using var downloader = new DownloadService(new DownloadConfiguration
                {
                    ChunkCount = item.Size >= ChunkThreshold ? 8 : 1,
                    ParallelDownload = item.Size >= ChunkThreshold,
                    MaxTryAgainOnFailure = 2,
                    BlockTimeout = 5000,
                    HttpClientTimeout = 60000,
                    ClearPackageOnCompletionWithFailure = true,
                    FileExistPolicy = FileExistPolicy.Delete,
                    CustomHttpClientFactory = () => _client
                });

                await downloader
                    .DownloadFileTaskAsync(url, item.Path, cancellationToken)
                    .ConfigureAwait(false);

                if (!File.Exists(item.Path))
                    throw new IOException($"Download produced no file: {url}");

                if (item.Size > 0 && new FileInfo(item.Path).Length != item.Size)
                    throw new IOException($"Download size mismatch: {url}");

                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                last = exception;
                TryDelete(item.Path);
            }
        }

        throw new IOException($"Download failed: {item.Resource.Url}", last);
    }

    private static bool IsReady(string path, long size) =>
        File.Exists(path) && (size <= 0 || new FileInfo(path).Length == size);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
