// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;

namespace UML.Core.Minecraft;

public sealed class MinecraftInstanceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MinecraftInstanceStore(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        GameDirectory = Path.GetFullPath(gameDirectory);
    }

    public string GameDirectory { get; }

    public static string DefaultDirectory =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".minecraft");

    public string GetVersionDirectory(string id)
    {
        ValidateId(id);
        return Path.Combine(GameDirectory, "versions", id);
    }

    public string GetVersionJsonPath(string id) =>
        Path.Combine(GetVersionDirectory(id), id + ".json");

    public IReadOnlyList<MinecraftInstalledVersion> GetVersions()
    {
        var versionsDirectory = Path.Combine(GameDirectory, "versions");
        if (!Directory.Exists(versionsDirectory)) return [];

        var result = new List<MinecraftInstalledVersion>();

        foreach (var directory in Directory.EnumerateDirectories(versionsDirectory))
        {
            var name = Path.GetFileName(directory);
            var jsonPath = Path.Combine(directory, name + ".json");
            if (!File.Exists(jsonPath)) continue;

            try
            {
                var detail = JsonSerializer.Deserialize<MinecraftVersionDetail>(
                    File.ReadAllText(jsonPath), JsonOptions);

                result.Add(new MinecraftInstalledVersion(
                    name,
                    detail?.Id ?? name,
                    detail?.Type,
                    detail?.InheritsFrom,
                    directory));
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        return result
            .OrderBy(version => version.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public MinecraftVersionDetail? GetVersion(string id)
    {
        var path = GetVersionJsonPath(id);
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<MinecraftVersionDetail>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public bool IsInstalled(string id) => File.Exists(GetVersionJsonPath(id));

    public bool Remove(string id)
    {
        var directory = GetVersionDirectory(id);
        if (!Directory.Exists(directory)) return false;

        Directory.Delete(directory, true);
        return true;
    }

    public void WriteVersionJson(string id, string json)
    {
        var directory = GetVersionDirectory(id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, id + ".json"), json);
    }

    public static void ValidateId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (id.Contains("..") || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"Invalid version id '{id}'.", nameof(id));
    }
}
