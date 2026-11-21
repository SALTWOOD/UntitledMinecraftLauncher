// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.InteropServices;

namespace UML.Core.Minecraft;

public static class MinecraftLibraries
{
    public static string? GetPath(MinecraftLibrary library)
    {
        if (!string.IsNullOrWhiteSpace(library.Downloads?.Artifact?.Path))
            return library.Downloads.Artifact.Path;

        return string.IsNullOrWhiteSpace(library.Name) ? null : MavenPath(library.Name);
    }

    public static MinecraftDownloadFile? GetNative(MinecraftLibrary library)
    {
        if (library.Natives is null || library.Downloads?.Classifiers is null)
            return null;

        var osName = MinecraftRules.CurrentOsName;
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "64",
            Architecture.X86 => "32",
            Architecture.Arm64 => "arm64",
            _ => "64"
        };

        if (library.Natives.TryGetValue(osName, out var pattern) &&
            library.Downloads.Classifiers.TryGetValue(pattern.Replace("${arch}", arch), out var native))
            return native;

        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            _ => "x86_64"
        };

        foreach (var candidate in new[]
                 {
                     $"natives-{osName}-{architecture}",
                     $"natives-{osName}"
                 })
            if (library.Downloads.Classifiers.TryGetValue(candidate, out var fallback))
                return fallback;

        return null;
    }

    public static string MavenPath(string name)
    {
        var extension = "jar";
        var at = name.IndexOf('@');
        if (at >= 0)
        {
            extension = name[(at + 1)..];
            name = name[..at];
        }

        var parts = name.Split(':');
        if (parts.Length < 3) return name.Replace(':', '/');

        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : string.Empty;

        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.{extension}";
    }
}
