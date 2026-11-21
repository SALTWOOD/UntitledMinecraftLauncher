// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Minecraft;

public enum MinecraftResourceKind
{
    VersionManifest,
    VersionJson,
    ClientJar,
    Library,
    AssetIndex,
    Asset,
    LoggingConfig,
    Loader,
    Other
}

public sealed record MinecraftResource(
    MinecraftResourceKind Kind,
    string Url,
    string? Id = null);

public interface IMinecraftSource
{
    string Name { get; }
    string ManifestUrl { get; }
    IReadOnlyList<string> Resolve(MinecraftResource resource);
}

public sealed class OfficialMinecraftSource : IMinecraftSource
{
    public const string DefaultManifestUrl =
        "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";

    public string Name => "Mojang";

    public string ManifestUrl => DefaultManifestUrl;

    public IReadOnlyList<string> Resolve(MinecraftResource resource) => [resource.Url];
}
