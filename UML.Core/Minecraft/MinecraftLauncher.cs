// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UML.Core.Minecraft;

public sealed class MinecraftLauncher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex PlaceholderPattern =
        new(@"\$\{([^}]+)\}", RegexOptions.Compiled);

    public MinecraftLaunchCommand Build(
        MinecraftVersionDetail version,
        MinecraftLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(version.InheritsFrom))
            throw new NotSupportedException(
                $"Version '{version.Id}' inherits from '{version.InheritsFrom}'; only vanilla versions are supported.");

        var gameDirectory = Path.GetFullPath(options.GameDirectory);
        var versionDirectory = Path.Combine(gameDirectory, "versions", version.Id);
        var nativesDirectory = Path.Combine(versionDirectory, "natives");
        var assetsDirectory = Path.Combine(gameDirectory, "assets");
        var libraryDirectory = Path.Combine(gameDirectory, "libraries");

        var classpath = version.Libraries
            .Where(library => MinecraftRules.Allows(library.Rules))
            .Select(MinecraftLibraries.GetPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.Combine(libraryDirectory, path!))
            .Append(Path.Combine(versionDirectory, version.Id + ".jar"))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var quickPlay = options.QuickPlay;
        var features = new MinecraftFeatures(
            IsDemoUser: options.Demo,
            HasCustomResolution: options.Width is not null && options.Height is not null,
            HasQuickPlaysSupport: quickPlay is not null,
            QuickPlaySingleplayer: quickPlay?.Kind == MinecraftQuickPlayKind.Singleplayer,
            QuickPlayMultiplayer: quickPlay?.Kind == MinecraftQuickPlayKind.Multiplayer,
            QuickPlayRealms: quickPlay?.Kind == MinecraftQuickPlayKind.Realms);

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = options.UserName,
            ["version_name"] = version.Id,
            ["game_directory"] = gameDirectory,
            ["assets_root"] = assetsDirectory,
            ["assets_index_name"] = version.AssetIndex?.Id ?? version.Assets ?? "legacy",
            ["auth_uuid"] = options.Uuid.ToString("N"),
            ["auth_access_token"] = options.AccessToken,
            ["user_type"] = options.UserType,
            ["version_type"] = version.Type ?? "release",
            ["natives_directory"] = nativesDirectory,
            ["launcher_name"] = options.LauncherName,
            ["launcher_version"] = options.LauncherVersion,
            ["classpath"] = string.Join(Path.PathSeparator, classpath),
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["library_directory"] = libraryDirectory,
            ["clientid"] = options.ClientId ?? string.Empty,
            ["auth_xuid"] = options.Xuid ?? string.Empty,
            ["game_assets"] = Path.Combine(assetsDirectory, "virtual", "legacy"),
            ["resolution_width"] = options.Width?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["resolution_height"] = options.Height?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["quickPlayPath"] = Path.Combine(gameDirectory, "quickPlay.log"),
            ["quickPlaySingleplayer"] = quickPlay?.Kind == MinecraftQuickPlayKind.Singleplayer
                ? quickPlay.Value ?? string.Empty
                : string.Empty,
            ["quickPlayMultiplayer"] = quickPlay?.Kind == MinecraftQuickPlayKind.Multiplayer
                ? quickPlay.Value ?? string.Empty
                : string.Empty,
            ["quickPlayRealms"] = quickPlay?.Kind == MinecraftQuickPlayKind.Realms
                ? quickPlay.Value ?? string.Empty
                : string.Empty
        };

        var jvmArguments = new List<string>();

        if (version.Arguments?.Jvm is { Count: > 0 } jvmEntries)
        {
            AddArgumentEntries(jvmEntries, features, placeholders, jvmArguments);
        }
        else
        {
            jvmArguments.Add("-Djava.library.path=" + nativesDirectory);
            jvmArguments.Add("-cp");
            jvmArguments.Add(placeholders["classpath"]);
        }

        if (!jvmArguments.Contains("-cp") && !jvmArguments.Contains("-classpath"))
        {
            jvmArguments.Add("-cp");
            jvmArguments.Add(placeholders["classpath"]);
        }

        if (version.Logging?.Client is { Argument: { } loggingArgument, File.Id: { } logId })
        {
            var logPath = Path.Combine(assetsDirectory, "log_configs", logId);
            if (File.Exists(logPath))
                jvmArguments.Insert(0, loggingArgument.Replace("${path}", logPath));
        }

        jvmArguments.AddRange(options.ExtraJvmArguments);

        var gameArguments = new List<string>();

        if (version.Arguments?.Game is { Count: > 0 } gameEntries)
        {
            AddArgumentEntries(gameEntries, features, placeholders, gameArguments);
        }
        else if (!string.IsNullOrWhiteSpace(version.LegacyGameArguments))
        {
            foreach (var token in SplitArguments(version.LegacyGameArguments))
                gameArguments.Add(Resolve(token, placeholders));
        }
        else
        {
            gameArguments.AddRange(DefaultGameArguments(placeholders));
        }

        gameArguments.AddRange(options.ExtraGameArguments);

        if (string.IsNullOrWhiteSpace(version.MainClass))
            throw new InvalidDataException($"Version '{version.Id}' has no main class.");

        return new MinecraftLaunchCommand(
            ResolveJavaPath(options.JavaPath),
            gameDirectory,
            version.MainClass,
            jvmArguments,
            gameArguments);
    }

    public static string ResolveJavaPath(string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        var home = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            var executable = Path.Combine(
                home,
                "bin",
                OperatingSystem.IsWindows() ? "java.exe" : "java");

            if (File.Exists(executable)) return executable;
        }

        return OperatingSystem.IsWindows() ? "java.exe" : "java";
    }

    public static Process Start(MinecraftLaunchCommand command, bool redirectOutput = false)
    {
        ArgumentNullException.ThrowIfNull(command);

        var startInfo = new ProcessStartInfo(command.JavaPath)
        {
            WorkingDirectory = command.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput
        };

        foreach (var argument in command.ToArguments())
            startInfo.ArgumentList.Add(argument);

        return Process.Start(startInfo)
               ?? throw new InvalidOperationException("Failed to start the Java process.");
    }

    private static void AddArgumentEntries(
        IEnumerable<JsonElement> entries,
        in MinecraftFeatures features,
        IReadOnlyDictionary<string, string> placeholders,
        List<string> output)
    {
        foreach (var entry in entries)
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                output.Add(Resolve(entry.GetString()!, placeholders));
                continue;
            }

            if (entry.ValueKind != JsonValueKind.Object) continue;

            if (entry.TryGetProperty("rules", out var rulesElement))
            {
                var rules = rulesElement.Deserialize<List<MinecraftRule>>(JsonOptions);
                if (!MinecraftRules.Allows(rules, features)) continue;
            }

            if (!entry.TryGetProperty("value", out var value)) continue;

            if (value.ValueKind == JsonValueKind.String)
            {
                output.Add(Resolve(value.GetString()!, placeholders));
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String)
                        output.Add(Resolve(item.GetString()!, placeholders));
            }
        }
    }

    private static IEnumerable<string> DefaultGameArguments(
        IReadOnlyDictionary<string, string> placeholders)
    {
        yield return "--username";
        yield return placeholders["auth_player_name"];
        yield return "--version";
        yield return placeholders["version_name"];
        yield return "--gameDir";
        yield return placeholders["game_directory"];
        yield return "--assetsDir";
        yield return placeholders["assets_root"];
        yield return "--assetIndex";
        yield return placeholders["assets_index_name"];
        yield return "--uuid";
        yield return placeholders["auth_uuid"];
        yield return "--accessToken";
        yield return placeholders["auth_access_token"];
        yield return "--userType";
        yield return placeholders["user_type"];
        yield return "--versionType";
        yield return placeholders["version_type"];
    }

    private static string Resolve(
        string value,
        IReadOnlyDictionary<string, string> placeholders) =>
        PlaceholderPattern.Replace(
            value,
            match => placeholders.TryGetValue(match.Groups[1].Value, out var replacement)
                ? replacement
                : string.Empty);

    private static IEnumerable<string> SplitArguments(string text)
    {
        var current = new StringBuilder();
        var quote = '\0';

        foreach (var character in text)
        {
            if (quote != '\0')
            {
                if (character == quote) quote = '\0';
                else current.Append(character);
            }
            else if (character is '"' or '\'')
            {
                quote = character;
            }
            else if (char.IsWhiteSpace(character))
            {
                if (current.Length == 0) continue;
                yield return current.ToString();
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        if (current.Length > 0) yield return current.ToString();
    }
}
