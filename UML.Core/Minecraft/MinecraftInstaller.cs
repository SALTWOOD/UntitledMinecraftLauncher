// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO.Compression;
using System.Text.Json;

namespace UML.Core.Minecraft;

public interface IMinecraftInstaller
{
    string Name { get; }

    Task InstallAsync(
        string id,
        IProgress<MinecraftInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class VanillaMinecraftInstaller : IMinecraftInstaller
{
    private const string AssetsRoot = "https://resources.download.minecraft.net";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly MinecraftInstanceStore _store;
    private readonly MinecraftDownloader _downloader;

    public VanillaMinecraftInstaller(
        MinecraftInstanceStore store,
        MinecraftDownloader downloader)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(downloader);

        _store = store;
        _downloader = downloader;
    }

    public string Name => "vanilla";

    public async Task InstallAsync(
        string id,
        IProgress<MinecraftInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        MinecraftInstanceStore.ValidateId(id);

        var manifest = await FetchManifestAsync(cancellationToken).ConfigureAwait(false);
        var entry = manifest.Versions.FirstOrDefault(version => version.Id == id)
                    ?? throw new KeyNotFoundException($"Minecraft version '{id}' was not found.");

        var (detail, json) = await FetchVersionAsync(entry, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(detail.InheritsFrom))
            throw new NotSupportedException(
                $"Version '{id}' inherits from '{detail.InheritsFrom}'; only vanilla versions are supported.");

        _store.WriteVersionJson(id, json);
        progress?.Report(new MinecraftInstallProgress("version", 1, 1));

        var versionDirectory = _store.GetVersionDirectory(id);
        var librariesDirectory = Path.Combine(_store.GameDirectory, "libraries");
        var items = new List<MinecraftDownloadItem>();
        var natives = new List<string>();

        if (detail.Downloads?.Client is { Url: { } clientUrl } client)
            items.Add(new MinecraftDownloadItem(
                new MinecraftResource(MinecraftResourceKind.ClientJar, clientUrl, id),
                Path.Combine(versionDirectory, id + ".jar"),
                client.Size));

        foreach (var library in detail.Libraries.Where(library => MinecraftRules.Allows(library.Rules)))
        {
            if (library.Downloads?.Artifact is { Url: { } artifactUrl } artifact)
            {
                var relative = MinecraftLibraries.GetPath(library);
                if (relative is not null)
                    items.Add(new MinecraftDownloadItem(
                        new MinecraftResource(MinecraftResourceKind.Library, artifactUrl),
                        Path.Combine(librariesDirectory, relative),
                        artifact.Size));
            }

            if (MinecraftLibraries.GetNative(library) is { Url: { } nativeUrl } native)
            {
                var relative = native.Path ?? MinecraftLibraries.MavenPath(library.Name);
                var path = Path.Combine(librariesDirectory, relative);

                items.Add(new MinecraftDownloadItem(
                    new MinecraftResource(MinecraftResourceKind.Library, nativeUrl),
                    path,
                    native.Size));

                natives.Add(path);
            }
        }

        await _downloader
            .DownloadAsync(items, "libraries", progress, cancellationToken)
            .ConfigureAwait(false);
        items.Clear();

        if (detail.AssetIndex is { } assetIndex)
            await InstallAssetsAsync(detail, assetIndex, items, progress, cancellationToken)
                .ConfigureAwait(false);

        ExtractNatives(natives, Path.Combine(versionDirectory, "natives"));

        progress?.Report(new MinecraftInstallProgress("done", 1, 1));
    }

    private async Task<MinecraftManifest> FetchManifestAsync(CancellationToken cancellationToken)
    {
        var json = await _downloader.GetStringAsync(
                new MinecraftResource(
                    MinecraftResourceKind.VersionManifest,
                    _downloader.Source.ManifestUrl),
                cancellationToken)
            .ConfigureAwait(false);

        return JsonSerializer.Deserialize<MinecraftManifest>(json, JsonOptions)
               ?? throw new InvalidDataException("Minecraft version manifest is empty.");
    }

    private async Task<(MinecraftVersionDetail Detail, string Json)> FetchVersionAsync(
        MinecraftVersionSummary entry,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entry.Url))
            throw new InvalidDataException($"Version '{entry.Id}' has no JSON URL.");

        var json = await _downloader.GetStringAsync(
                new MinecraftResource(MinecraftResourceKind.VersionJson, entry.Url, entry.Id),
                cancellationToken)
            .ConfigureAwait(false);

        var detail = JsonSerializer.Deserialize<MinecraftVersionDetail>(json, JsonOptions)
                     ?? throw new InvalidDataException($"Version '{entry.Id}' JSON is empty.");

        return (detail, json);
    }

    private async Task InstallAssetsAsync(
        MinecraftVersionDetail detail,
        MinecraftAssetIndex assetIndex,
        List<MinecraftDownloadItem> items,
        IProgress<MinecraftInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var indexPath = Path.Combine(
            _store.GameDirectory,
            "assets",
            "indexes",
            assetIndex.Id + ".json");

        if (!IsReady(indexPath, assetIndex.Size) && assetIndex.Url is { } indexUrl)
            await _downloader
                .DownloadAsync(
                    [
                        new MinecraftDownloadItem(
                            new MinecraftResource(MinecraftResourceKind.AssetIndex, indexUrl, assetIndex.Id),
                            indexPath,
                            assetIndex.Size)
                    ],
                    "index",
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

        if (!File.Exists(indexPath)) return;

        var index = JsonSerializer.Deserialize<MinecraftAssetIndexManifest>(
                        await File.ReadAllTextAsync(indexPath, cancellationToken).ConfigureAwait(false),
                        JsonOptions)
                    ?? new MinecraftAssetIndexManifest();

        foreach (var (_, asset) in index.Objects)
        {
            if (asset.Hash.Length < 2) continue;

            items.Add(new MinecraftDownloadItem(
                new MinecraftResource(
                    MinecraftResourceKind.Asset,
                    $"{AssetsRoot}/{asset.Hash[..2]}/{asset.Hash}",
                    asset.Hash),
                Path.Combine(_store.GameDirectory, "assets", "objects", asset.Hash[..2], asset.Hash),
                asset.Size));
        }

        if (detail.Logging?.Client?.File is { Url: { } logUrl, Id: { } logId } log)
            items.Add(new MinecraftDownloadItem(
                new MinecraftResource(MinecraftResourceKind.LoggingConfig, logUrl, logId),
                Path.Combine(_store.GameDirectory, "assets", "log_configs", logId),
                log.Size));

        await _downloader
            .DownloadAsync(items, "assets", progress, cancellationToken)
            .ConfigureAwait(false);

        if (index.Virtual || index.MapToResources)
            CopyVirtualAssets(index, assetIndex.Id);
    }

    private void CopyVirtualAssets(MinecraftAssetIndexManifest index, string indexId)
    {
        var root = index.Virtual
            ? Path.Combine(_store.GameDirectory, "assets", "virtual", indexId)
            : Path.Combine(_store.GameDirectory, "resources");

        foreach (var (name, asset) in index.Objects)
        {
            var source = Path.Combine(
                _store.GameDirectory,
                "assets",
                "objects",
                asset.Hash[..2],
                asset.Hash);

            if (!File.Exists(source)) continue;

            var destination = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
        }
    }

    private static void ExtractNatives(IEnumerable<string> jars, string targetDirectory)
    {
        var found = jars.Distinct().Where(File.Exists).ToList();
        if (found.Count == 0) return;

        Directory.CreateDirectory(targetDirectory);

        foreach (var jar in found)
        {
            using var archive = ZipFile.OpenRead(jar);

            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/') ||
                    entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
                    continue;

                var destination = Path.Combine(targetDirectory, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, true);
            }
        }
    }

    private static bool IsReady(string path, long size) =>
        File.Exists(path) && (size <= 0 || new FileInfo(path).Length == size);
}
