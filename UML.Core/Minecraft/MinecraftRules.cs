// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace UML.Core.Minecraft;

public static class MinecraftRules
{
    public static string CurrentOsName =>
        OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "osx"
        : "linux";

    public static bool Allows(
        IReadOnlyCollection<MinecraftRule>? rules,
        MinecraftFeatures features = default)
    {
        if (rules is null or { Count: 0 }) return true;

        var allowed = false;
        foreach (var rule in rules)
        {
            if (!Matches(rule, features)) continue;
            allowed = rule.Action == "allow";
        }

        return allowed;
    }

    private static bool Matches(MinecraftRule rule, in MinecraftFeatures features)
    {
        if (rule.Os is { } os && !MatchesOs(os)) return false;
        if (rule.Features is { } ruleFeatures && !MatchesFeatures(ruleFeatures, features)) return false;
        return true;
    }

    private static bool MatchesOs(MinecraftRuleOs os)
    {
        if (os.Name is { } name &&
            name != "unknown" &&
            !string.Equals(name, CurrentOsName, StringComparison.Ordinal))
            return false;

        if (os.Arch is { } arch && !MatchesArch(arch)) return false;

        if (os.Version is { Length: > 0 } version)
        {
            try
            {
                if (!Regex.IsMatch(Environment.OSVersion.Version.ToString(), version))
                    return false;
            }
            catch (ArgumentException)
            {
            }
        }

        return true;
    }

    private static bool MatchesArch(string arch) =>
        arch switch
        {
            "x86" => RuntimeInformation.ProcessArchitecture == Architecture.X86,
            "x86_64" or "amd64" => RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "arm64" or "aarch64" => RuntimeInformation.ProcessArchitecture == Architecture.Arm64,
            _ => true
        };

    private static bool MatchesFeatures(MinecraftRuleFeatures rule, in MinecraftFeatures actual) =>
        (rule.IsDemoUser is not { } demo || demo == actual.IsDemoUser) &&
        (rule.HasCustomResolution is not { } resolution ||
         resolution == actual.HasCustomResolution) &&
        (rule.HasQuickPlaysSupport is not { } quick || quick == actual.HasQuickPlaysSupport) &&
        (rule.IsQuickPlaySingleplayer is not { } single || single == actual.QuickPlaySingleplayer) &&
        (rule.IsQuickPlayMultiplayer is not { } multi || multi == actual.QuickPlayMultiplayer) &&
        (rule.IsQuickPlayRealms is not { } realms || realms == actual.QuickPlayRealms);
}
