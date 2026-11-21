// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.CommandLine;
using System.Reflection;
using UML.Core;
using UML.Core.App;
using UML.Core.Authentication;
using UML.Core.Authentication.Microsoft;
using UML.Core.Configuration;
using UML.Core.Minecraft;
using UML.Core.Profile;

namespace UML.Cli;

internal static class Program
{
    private static bool _exitRequested;

    private static async Task<int> Main(string[] args)
    {
        var (globalPath, localPath) = ResolveConfigPaths();
        await ConfigHost.InitializeAsync(globalPath, localPath);

        try
        {
            PrintInfo(globalPath, localPath);

            var root = BuildCommandTree();
            if (args.Length > 0)
                return await root.Parse(args).InvokeAsync();

            return await RunInteractiveAsync(root);
        }
        finally
        {
            await ConfigHost.ShutdownAsync();
        }
    }

    private static async Task<int> RunInteractiveAsync(RootCommand root)
    {
        while (!_exitRequested)
        {
            Console.Write("> ");

            var line = Console.ReadLine();
            if (line is null) break;

            line = line.Trim();
            if (line.Length == 0) continue;

            if (line is "help" or "?")
            {
                await root.Parse("--help").InvokeAsync();
                continue;
            }

            await root.Parse(line).InvokeAsync();
        }

        return 0;
    }

    private static RootCommand BuildCommandTree()
    {
        var root = new RootCommand("UML command line interface");

        var profile = new Command("profile", "Manage user profiles");

        var add = new Command("add", "Add a profile");

        var microsoft = new Command("microsoft", "Sign in with a Microsoft account");
        microsoft.SetAction(async (_, cancellationToken) => await AddMicrosoftAsync(cancellationToken));

        var offline = new Command("offline", "Add an offline profile");
        var usernameArgument = new Argument<string>("username")
        {
            Description = "Offline username"
        };
        var uuidArgument = new Argument<string>("uuid")
        {
            Description = "Optional profile UUID",
            Arity = ArgumentArity.ZeroOrOne
        };
        offline.Arguments.Add(usernameArgument);
        offline.Arguments.Add(uuidArgument);
        offline.SetAction(async (parseResult, cancellationToken) =>
            await AddOfflineAsync(
                parseResult.GetValue(usernameArgument)!,
                parseResult.GetValue(uuidArgument),
                cancellationToken));

        add.Subcommands.Add(microsoft);
        add.Subcommands.Add(offline);

        var remove = new Command("remove", "Remove a profile");
        var removeTarget = new Argument<string>("target") { Description = "Profile UUID, account id or username" };
        remove.Arguments.Add(removeTarget);
        remove.SetAction(async (parseResult, cancellationToken) =>
            await RemoveAsync(parseResult.GetValue(removeTarget)!, cancellationToken));

        var selectProfile = new Command("select", "Select the current profile");
        var selectTarget = new Argument<string>("target") { Description = "Profile UUID, account id or username" };
        selectProfile.Arguments.Add(selectTarget);
        selectProfile.SetAction(async (parseResult, cancellationToken) =>
            await SelectAsync(parseResult.GetValue(selectTarget)!, cancellationToken));

        var list = new Command("list", "List profiles");
        list.SetAction(_ => ListProfiles());

        profile.Subcommands.Add(add);
        profile.Subcommands.Add(remove);
        profile.Subcommands.Add(selectProfile);
        profile.Subcommands.Add(list);

        var minecraft = new Command("minecraft", "Manage vanilla Minecraft instances");

        var minecraftList = new Command("list", "List installed versions");
        minecraftList.SetAction(_ => ListMinecraftVersions());

        var minecraftInstall = new Command("install", "Install a vanilla version");
        var installArgument = new Argument<string>("version") { Description = "Version id" };
        minecraftInstall.Arguments.Add(installArgument);
        minecraftInstall.SetAction(async (parseResult, cancellationToken) =>
            await InstallMinecraftAsync(parseResult.GetValue(installArgument)!, cancellationToken));

        var minecraftRemove = new Command("remove", "Remove an installed version");
        var minecraftRemoveArgument = new Argument<string>("version") { Description = "Version id" };
        minecraftRemove.Arguments.Add(minecraftRemoveArgument);
        minecraftRemove.SetAction(async (parseResult, cancellationToken) =>
            await RemoveMinecraftAsync(parseResult.GetValue(minecraftRemoveArgument)!, cancellationToken));

        minecraft.Subcommands.Add(minecraftList);
        minecraft.Subcommands.Add(minecraftInstall);
        minecraft.Subcommands.Add(minecraftRemove);

        var launch = new Command("launch", "Launch the selected Minecraft instance");
        var dryRun = new Option<bool>("--dry-run", "-n")
        {
            Description = "Print the command without starting the game"
        };
        var log = new Option<bool>("--log", "-l")
        {
            Description = "Stream the game's stdout/stderr and wait for it to exit"
        };
        launch.Options.Add(dryRun);
        launch.Options.Add(log);
        launch.SetAction(async (parseResult, cancellationToken) =>
            await LaunchAsync(
                parseResult.GetValue(dryRun),
                parseResult.GetValue(log),
                cancellationToken));

        var select = new Command("select", "Select the Minecraft folder or instance");
        var versionArgument = new Argument<string>("version")
        {
            Description = "Installed version id",
            Arity = ArgumentArity.ZeroOrOne
        };
        select.Arguments.Add(versionArgument);
        select.SetAction(async (parseResult, cancellationToken) =>
        {
            var version = parseResult.GetValue(versionArgument);
            return string.IsNullOrWhiteSpace(version)
                ? ShowMinecraftSelection()
                : await SelectMinecraftVersionAsync(version, cancellationToken);
        });

        var folder = new Command("folder", "Select the .minecraft folder");
        var folderArgument = new Argument<string>("path") { Description = "Path to the .minecraft folder" };
        folder.Arguments.Add(folderArgument);
        folder.SetAction(async (parseResult, cancellationToken) =>
            await SelectMinecraftFolderAsync(parseResult.GetValue(folderArgument)!, cancellationToken));
        select.Subcommands.Add(folder);

        var exit = new Command("exit", "Exit the CLI");
        exit.Aliases.Add("quit");
        exit.SetAction(_ => _exitRequested = true);

        root.Subcommands.Add(profile);
        root.Subcommands.Add(minecraft);
        root.Subcommands.Add(launch);
        root.Subcommands.Add(select);
        root.Subcommands.Add(exit);

        return root;
    }

    private static async Task<int> AddMicrosoftAsync(CancellationToken cancellationToken)
    {
        try
        {
            var login = new MicrosoftLogin(new MicrosoftLoginOptions());
            var account = await login.AuthenticateWithDeviceCodeAsync(code =>
            {
                Console.WriteLine();
                Console.WriteLine("Open this URL in a browser:");
                Console.WriteLine("  " + (code.VerificationUriComplete ?? code.VerificationUri ?? "<missing>"));
                Console.WriteLine();
                Console.WriteLine("Enter code: " + code.UserCode);
                Console.WriteLine();
                Console.WriteLine("Waiting for authorization...");
            }, cancellationToken);

            var profile = ProfileManager.Current.Add(account);
            await ProfileManager.Current.SaveAsync(cancellationToken);

            Console.WriteLine($"Added Microsoft profile {profile.Username} ({profile.Uuid}).");
            return 0;
        }
        catch (Exception exception)
            when (exception is AuthenticationException
                      or HttpRequestException
                      or InvalidOperationException
                      or OperationCanceledException)
        {
            Error(exception is AuthenticationException
                ? exception.Message
                : exception.GetBaseException().Message);
            return 1;
        }
    }

    private static async Task<int> AddOfflineAsync(
        string username,
        string? uuidText,
        CancellationToken cancellationToken)
    {
        Guid? uuid = null;
        if (!string.IsNullOrWhiteSpace(uuidText))
        {
            if (!Guid.TryParse(uuidText.Trim(), out var parsed))
            {
                Error($"'{uuidText}' is not a valid UUID.");
                return 1;
            }

            uuid = parsed;
        }

        try
        {
            var profile = ProfileManager.Current.AddOffline(username, uuid);
            await ProfileManager.Current.SaveAsync(cancellationToken);

            Console.WriteLine($"Added offline profile {profile.Username} ({profile.Uuid}).");
            return 0;
        }
        catch (ArgumentException exception)
        {
            Error(exception.Message);
            return 1;
        }
    }

    private static async Task<int> RemoveAsync(string target, CancellationToken cancellationToken)
    {
        var profile = ResolveProfile(target);
        if (profile is null)
        {
            Error($"No profile matches '{target}'.");
            return 1;
        }

        if (!ProfileManager.Current.Remove(profile))
        {
            Error($"Failed to remove '{target}'.");
            return 1;
        }

        await ProfileManager.Current.SaveAsync(cancellationToken);
        Console.WriteLine($"Removed {GetUsername(profile)} ({GetUuid(profile)}).");
        return 0;
    }

    private static async Task<int> SelectAsync(string target, CancellationToken cancellationToken)
    {
        var profile = ResolveProfile(target);
        if (profile is null)
        {
            Error($"No profile matches '{target}'.");
            return 1;
        }

        if (!ProfileManager.Current.Select(profile))
        {
            Error($"Failed to select '{target}'.");
            return 1;
        }

        await ProfileManager.Current.SaveAsync(cancellationToken);
        Console.WriteLine($"Selected {GetUsername(profile)} ({GetUuid(profile)}).");
        return 0;
    }

    private static int ListProfiles()
    {
        var manager = ProfileManager.Current;
        if (manager.Profiles.Count == 0)
        {
            Console.WriteLine("No profiles.");
            return 0;
        }

        var current = manager.CurrentProfile;

        Console.WriteLine($"{"UUID",-36}  {"TYPE",-10}  {"USERNAME",-20}  CURRENT");
        foreach (var profile in manager.Profiles)
        {
            var uuid = GetUuid(profile);
            var type = profile switch
            {
                MicrosoftProfile => "microsoft",
                OfflineProfile => "offline",
                _ => "unknown"
            };
            var marker = current is not null && GetUuid(current) == uuid ? "*" : string.Empty;

            Console.WriteLine($"{uuid,-36}  {type,-10}  {GetUsername(profile),-20}  {marker}");
        }

        return 0;
    }

    private static string GetMinecraftDirectory()
    {
        if (!string.IsNullOrWhiteSpace(Config.MinecraftDirectory))
            return Config.MinecraftDirectory!;

        var directory = MinecraftInstanceStore.DefaultDirectory;
        Config.MinecraftDirectory = directory;
        return directory;
    }

    private static async Task<int> SelectMinecraftFolderAsync(string path, CancellationToken cancellationToken)
    {
        string directory;

        try
        {
            directory = Path.GetFullPath(path.Trim());
            Directory.CreateDirectory(directory);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                      or IOException
                      or UnauthorizedAccessException
                      or NotSupportedException)
        {
            Error(exception.Message);
            return 1;
        }

        Config.MinecraftDirectory = directory;
        await ConfigService.Current.FlushAsync(cancellationToken);
        Console.WriteLine($"Minecraft folder: {directory}");
        return 0;
    }

    private static async Task<int> SelectMinecraftVersionAsync(string id, CancellationToken cancellationToken)
    {
        var directory = GetMinecraftDirectory();
        var store = new MinecraftInstanceStore(directory);

        try
        {
            if (!store.IsInstalled(id))
            {
                Error($"Version '{id}' is not installed in {directory}.");
                return 1;
            }
        }
        catch (ArgumentException exception)
        {
            Error(exception.Message);
            return 1;
        }

        Config.MinecraftVersion = id;
        await ConfigService.Current.FlushAsync(cancellationToken);
        Console.WriteLine($"Selected Minecraft version: {id}");
        return 0;
    }

    private static int ShowMinecraftSelection()
    {
        var directory = GetMinecraftDirectory();
        var version = Config.MinecraftVersion;

        Console.WriteLine($"Minecraft folder: {directory}");
        Console.WriteLine($"Minecraft version: {(string.IsNullOrWhiteSpace(version) ? "none" : version)}");
        return 0;
    }

    private static int ListMinecraftVersions()
    {
        var directory = GetMinecraftDirectory();
        var store = new MinecraftInstanceStore(directory);
        var versions = store.GetVersions();

        if (versions.Count == 0)
        {
            Console.WriteLine($"No versions installed in {directory}.");
            return 0;
        }

        var selected = Config.MinecraftVersion;

        Console.WriteLine($"{"VERSION",-24}  {"TYPE",-10}  {"INHERITS",-18}  CURRENT");
        foreach (var version in versions)
        {
            var marker = string.Equals(version.Name, selected, StringComparison.Ordinal) ? "*" : "";
            Console.WriteLine(
                $"{version.Name,-24}  {version.Type ?? "?",-10}  {version.InheritsFrom ?? "",-18}  {marker}");
        }

        return 0;
    }

    private static async Task<int> InstallMinecraftAsync(string id, CancellationToken cancellationToken)
    {
        var directory = GetMinecraftDirectory();
        var store = new MinecraftInstanceStore(directory);
        var installer = new VanillaMinecraftInstaller(
            store,
            new MinecraftDownloader(new OfficialMinecraftSource()));

        try
        {
            Console.WriteLine($"Installing {id} into {directory}...");
            await installer.InstallAsync(id, new ConsoleProgress(), cancellationToken);

            Config.MinecraftVersion = id;
            await ConfigService.Current.FlushAsync(cancellationToken);

            Console.WriteLine($"Installed and selected {id}.");
            return 0;
        }
        catch (Exception exception)
            when (exception is HttpRequestException
                      or IOException
                      or KeyNotFoundException
                      or NotSupportedException
                      or InvalidOperationException
                      or ArgumentException)
        {
            Error(exception.GetBaseException().Message);
            return 1;
        }
    }

    private static async Task<int> RemoveMinecraftAsync(string id, CancellationToken cancellationToken)
    {
        var directory = GetMinecraftDirectory();
        var store = new MinecraftInstanceStore(directory);

        try
        {
            if (!store.Remove(id))
            {
                Error($"Version '{id}' is not installed in {directory}.");
                return 1;
            }

            if (string.Equals(Config.MinecraftVersion, id, StringComparison.Ordinal))
            {
                Config.MinecraftVersion = null;
                await ConfigService.Current.FlushAsync(cancellationToken);
            }

            Console.WriteLine($"Removed {id}.");
            return 0;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Error(exception.Message);
            return 1;
        }
    }

    private static async Task<int> LaunchAsync(
        bool dryRun,
        bool log,
        CancellationToken cancellationToken)
    {
        var directory = GetMinecraftDirectory();
        var versionId = Config.MinecraftVersion;

        if (string.IsNullOrWhiteSpace(versionId))
        {
            Error("No Minecraft version selected. Use 'select <version>'.");
            return 1;
        }

        var store = new MinecraftInstanceStore(directory);

        MinecraftVersionDetail? version;
        try
        {
            version = store.GetVersion(versionId);
        }
        catch (ArgumentException exception)
        {
            Error(exception.Message);
            return 1;
        }

        if (version is null)
        {
            Error($"Version '{versionId}' is not installed in {directory}.");
            return 1;
        }

        var profile = ProfileManager.Current.CurrentProfile;
        if (profile is null)
        {
            Error("No profile selected. Use 'profile select <id>'.");
            return 1;
        }

        var options = CreateLaunchOptions(directory, profile, out var warning);
        if (options is null) return 1;
        if (warning is not null) Console.Error.WriteLine("warning: " + warning);

        try
        {
            var command = new MinecraftLauncher().Build(version, options);

            if (dryRun)
            {
                Console.WriteLine(command.ToDisplayString());
                return 0;
            }

            using var process = MinecraftLauncher.Start(command, redirectOutput: log);
            Console.WriteLine(
                log
                    ? $"Launched {versionId} (PID {process.Id}). Streaming logs, press Ctrl+C to stop waiting..."
                    : $"Launched {versionId} (PID {process.Id}).");

            if (!log) return 0;

            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is not null) Console.WriteLine(eventArgs.Data);
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is not null) Console.Error.WriteLine(eventArgs.Data);
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
                process.WaitForExit();
            }
            catch (OperationCanceledException)
            {
                return 130;
            }

            Console.WriteLine($"Minecraft exited with code {process.ExitCode}.");
            return process.ExitCode;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                      or InvalidDataException
                      or NotSupportedException
                      or System.ComponentModel.Win32Exception)
        {
            Error(exception.GetBaseException().Message);
            return 1;
        }
    }

    private static MinecraftLaunchOptions? CreateLaunchOptions(
        string directory,
        UserProfile profile,
        out string? warning)
    {
        warning = null;
        var launcherVersion = GetVersion();

        switch (profile)
        {
            case MicrosoftProfile microsoft:
                if (string.IsNullOrWhiteSpace(microsoft.Tokens.MinecraftAccessToken))
                {
                    Error("The selected Microsoft profile has no Minecraft access token.");
                    return null;
                }

                if (microsoft.Tokens.MinecraftExpiresAt <= DateTimeOffset.UtcNow)
                    warning = "The Minecraft access token has expired; re-run 'profile add microsoft' if launch fails.";

                return new MinecraftLaunchOptions
                {
                    GameDirectory = directory,
                    UserName = microsoft.Username,
                    Uuid = microsoft.Uuid,
                    AccessToken = microsoft.Tokens.MinecraftAccessToken,
                    UserType = "msa",
                    LauncherVersion = launcherVersion
                };

            case OfflineProfile offline:
                return new MinecraftLaunchOptions
                {
                    GameDirectory = directory,
                    UserName = offline.Username,
                    Uuid = offline.Uuid,
                    AccessToken = "0",
                    UserType = "legacy",
                    LauncherVersion = launcherVersion
                };

            default:
                Error("The selected profile cannot launch the game.");
                return null;
        }
    }

    private sealed class ConsoleProgress : IProgress<MinecraftInstallProgress>
    {
        private readonly Lock _gate = new();
        private string? _stage;

        public void Report(MinecraftInstallProgress value)
        {
            lock (_gate)
            {
                if (_stage != value.Stage)
                {
                    if (_stage is not null) Console.WriteLine();
                    _stage = value.Stage;
                }

                if (value.Total == 0)
                {
                    Console.WriteLine($"{value.Stage}: nothing to download");
                    return;
                }

                Console.Write(
                    $"\r{value.Stage,-10} {value.Completed,5}/{value.Total,-5} {value.Percent,6:P0}");

                if (value.Completed >= value.Total) Console.WriteLine();
            }
        }
    }

    private static UserProfile? ResolveProfile(string target)
    {
        var manager = ProfileManager.Current;
        var value = target.Trim();

        if (Guid.TryParse(value, out var uuid))
        {
            var byUuid = manager.Find(uuid);
            if (byUuid is not null) return byUuid;
        }

        var byAccountId = manager.Profiles.FirstOrDefault(profile =>
            profile is MicrosoftProfile microsoft &&
            string.Equals(microsoft.Id, value, StringComparison.OrdinalIgnoreCase));

        return byAccountId
               ?? manager.Profiles.FirstOrDefault(profile =>
                   string.Equals(GetUsername(profile), value, StringComparison.OrdinalIgnoreCase));
    }

    private static Guid GetUuid(UserProfile profile) =>
        profile switch
        {
            MicrosoftProfile microsoft => microsoft.Uuid,
            OfflineProfile offline => offline.Uuid,
            _ => Guid.Empty
        };

    private static string GetUsername(UserProfile profile) =>
        profile switch
        {
            MicrosoftProfile microsoft => microsoft.Username,
            OfflineProfile offline => offline.Username,
            _ => string.Empty
        };

    private static void PrintInfo(string globalPath, string localPath)
    {
        var manager = ProfileManager.Current;
        var current = manager.CurrentProfile;
        var currentText = current is null
            ? "none"
            : $"{GetUsername(current)} ({GetUuid(current)})";

        Console.WriteLine($"UML CLI {GetVersion()}");
        Console.WriteLine($"Config: {globalPath}");
        Console.WriteLine($"        {localPath}");
        Console.WriteLine($"Profiles: {manager.Profiles.Count}, selected: {currentText}");
        Console.WriteLine(
            $"Minecraft: {Config.MinecraftVersion ?? "none"} @ " +
            (string.IsNullOrWhiteSpace(Config.MinecraftDirectory)
                ? MinecraftInstanceStore.DefaultDirectory + " (default)"
                : Config.MinecraftDirectory));
        Console.WriteLine(
            "Microsoft client id: " +
            (string.IsNullOrWhiteSpace(Secrets.MicrosoftClientId)
                ? "missing (set UML_MS_CLIENT_ID when debugging)"
                : "configured"));
        Console.WriteLine("Type 'help' for commands, 'exit' to quit.");
        Console.WriteLine();
    }

    private static string GetVersion()
    {
        var assembly = typeof(Program).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }

        return assembly.GetName().Version?.ToString(3) ?? "dev";
    }

    private static (string Global, string Local) ResolveConfigPaths()
    {
        var directory = Environment.GetEnvironmentVariable("UML_CONFIG_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "UML");
        }

        Directory.CreateDirectory(directory);

        return (Path.Combine(directory, "global.json"), Path.Combine(directory, "local.json"));
    }

    private static void Error(string message) => Console.Error.WriteLine("error: " + message);
}
