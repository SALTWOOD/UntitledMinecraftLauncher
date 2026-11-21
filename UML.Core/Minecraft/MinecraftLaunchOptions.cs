// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Minecraft;

public enum MinecraftQuickPlayKind
{
    Singleplayer,
    Multiplayer,
    Realms
}

public sealed record MinecraftQuickPlay(MinecraftQuickPlayKind Kind, string? Value = null);

public sealed record MinecraftLaunchOptions
{
    public required string GameDirectory { get; init; }
    public required string UserName { get; init; }
    public required Guid Uuid { get; init; }
    public required string AccessToken { get; init; }
    public string UserType { get; init; } = "msa";
    public string? Xuid { get; init; }
    public string? ClientId { get; init; }
    public string LauncherName { get; init; } = "UML";
    public string LauncherVersion { get; init; } = "1.0.0";
    public string? JavaPath { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public bool Demo { get; init; }
    public MinecraftQuickPlay? QuickPlay { get; init; }
    public IReadOnlyList<string> ExtraJvmArguments { get; init; } = [];
    public IReadOnlyList<string> ExtraGameArguments { get; init; } = [];
}

public sealed record MinecraftLaunchCommand(
    string JavaPath,
    string WorkingDirectory,
    string MainClass,
    IReadOnlyList<string> JvmArguments,
    IReadOnlyList<string> GameArguments)
{
    public IReadOnlyList<string> ToArguments() =>
    [
        .. JvmArguments,
        MainClass,
        .. GameArguments
    ];

    public string ToDisplayString() =>
        string.Join(
            ' ',
            new[] { JavaPath }
                .Concat(ToArguments())
                .Select(Quote));

    private static string Quote(string value) =>
        value.Length == 0 ? "\"\""
        : value.Any(char.IsWhiteSpace) ? $"\"{value}\""
        : value;
}
