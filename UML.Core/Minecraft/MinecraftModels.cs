// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization;

namespace UML.Core.Minecraft;

public sealed record MinecraftManifest
{
    [JsonPropertyName("latest")] public MinecraftLatest? Latest { get; init; }
    [JsonPropertyName("versions")] public List<MinecraftVersionSummary> Versions { get; init; } = [];
}

public sealed record MinecraftLatest
{
    [JsonPropertyName("release")] public string? Release { get; init; }
    [JsonPropertyName("snapshot")] public string? Snapshot { get; init; }
}

public sealed record MinecraftVersionSummary
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("url")] public string? Url { get; init; }
    [JsonPropertyName("sha1")] public string? Sha1 { get; init; }
    [JsonPropertyName("releaseTime")] public DateTimeOffset? ReleaseTime { get; init; }
}

public sealed record MinecraftVersionDetail
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("mainClass")] public string? MainClass { get; init; }
    [JsonPropertyName("inheritsFrom")] public string? InheritsFrom { get; init; }
    [JsonPropertyName("assets")] public string? Assets { get; init; }
    [JsonPropertyName("assetIndex")] public MinecraftAssetIndex? AssetIndex { get; init; }
    [JsonPropertyName("downloads")] public MinecraftDownloads? Downloads { get; init; }
    [JsonPropertyName("libraries")] public List<MinecraftLibrary> Libraries { get; init; } = [];
    [JsonPropertyName("logging")] public MinecraftLogging? Logging { get; init; }
    [JsonPropertyName("arguments")] public MinecraftArguments? Arguments { get; init; }
    [JsonPropertyName("minecraftArguments")] public string? LegacyGameArguments { get; init; }
    [JsonPropertyName("javaVersion")] public MinecraftJavaVersion? JavaVersion { get; init; }
}

public sealed record MinecraftArguments
{
    [JsonPropertyName("game")] public List<JsonElement>? Game { get; init; }
    [JsonPropertyName("jvm")] public List<JsonElement>? Jvm { get; init; }
}

public sealed record MinecraftJavaVersion
{
    [JsonPropertyName("component")] public string? Component { get; init; }
    [JsonPropertyName("majorVersion")] public int MajorVersion { get; init; }
}

public sealed record MinecraftDownloads
{
    [JsonPropertyName("client")] public MinecraftDownloadFile? Client { get; init; }
}

public sealed record MinecraftDownloadFile
{
    [JsonPropertyName("path")] public string? Path { get; init; }
    [JsonPropertyName("sha1")] public string? Sha1 { get; init; }
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("url")] public string? Url { get; init; }
}

public sealed record MinecraftAssetIndex
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("sha1")] public string? Sha1 { get; init; }
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("totalSize")] public long TotalSize { get; init; }
    [JsonPropertyName("url")] public string? Url { get; init; }
}

public sealed record MinecraftAssetIndexManifest
{
    [JsonPropertyName("objects")] public Dictionary<string, MinecraftAssetObject> Objects { get; init; } = new();
    [JsonPropertyName("virtual")] public bool Virtual { get; init; }
    [JsonPropertyName("map_to_resources")] public bool MapToResources { get; init; }
}

public sealed record MinecraftAssetObject
{
    [JsonPropertyName("hash")] public string Hash { get; init; } = string.Empty;
    [JsonPropertyName("size")] public long Size { get; init; }
}

public sealed record MinecraftLibrary
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("url")] public string? Url { get; init; }
    [JsonPropertyName("downloads")] public MinecraftLibraryDownloads? Downloads { get; init; }
    [JsonPropertyName("natives")] public Dictionary<string, string>? Natives { get; init; }
    [JsonPropertyName("rules")] public List<MinecraftRule>? Rules { get; init; }
}

public sealed record MinecraftLibraryDownloads
{
    [JsonPropertyName("artifact")] public MinecraftDownloadFile? Artifact { get; init; }

    [JsonPropertyName("classifiers")]
    public Dictionary<string, MinecraftDownloadFile>? Classifiers { get; init; }
}

public sealed record MinecraftRule
{
    [JsonPropertyName("action")] public string? Action { get; init; }
    [JsonPropertyName("os")] public MinecraftRuleOs? Os { get; init; }
    [JsonPropertyName("features")] public MinecraftRuleFeatures? Features { get; init; }
}

public sealed record MinecraftRuleOs
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("arch")] public string? Arch { get; init; }
    [JsonPropertyName("version")] public string? Version { get; init; }
}

public sealed record MinecraftRuleFeatures
{
    [JsonPropertyName("is_demo_user")] public bool? IsDemoUser { get; init; }
    [JsonPropertyName("has_custom_resolution")] public bool? HasCustomResolution { get; init; }

    [JsonPropertyName("has_quick_plays_support")]
    public bool? HasQuickPlaysSupport { get; init; }

    [JsonPropertyName("is_quick_play_singleplayer")]
    public bool? IsQuickPlaySingleplayer { get; init; }

    [JsonPropertyName("is_quick_play_multiplayer")]
    public bool? IsQuickPlayMultiplayer { get; init; }

    [JsonPropertyName("is_quick_play_realms")]
    public bool? IsQuickPlayRealms { get; init; }
}

public readonly record struct MinecraftFeatures(
    bool IsDemoUser = false,
    bool HasCustomResolution = false,
    bool HasQuickPlaysSupport = false,
    bool QuickPlaySingleplayer = false,
    bool QuickPlayMultiplayer = false,
    bool QuickPlayRealms = false);

public sealed record MinecraftLogging
{
    [JsonPropertyName("client")] public MinecraftLoggingClient? Client { get; init; }
}

public sealed record MinecraftLoggingClient
{
    [JsonPropertyName("argument")] public string? Argument { get; init; }
    [JsonPropertyName("file")] public MinecraftLoggingFile? File { get; init; }
}

public sealed record MinecraftLoggingFile
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("sha1")] public string? Sha1 { get; init; }
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("url")] public string? Url { get; init; }
}

public sealed record MinecraftInstalledVersion(
    string Name,
    string Id,
    string? Type,
    string? InheritsFrom,
    string Path);

public sealed record MinecraftInstallProgress(string Stage, int Completed, int Total)
{
    public double Percent => Total <= 0 ? 1d : (double)Completed / Total;
}
