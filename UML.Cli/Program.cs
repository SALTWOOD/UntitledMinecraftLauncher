// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.CommandLine;
using System.Reflection;
using UML.Core;
using UML.Core.App;
using UML.Core.Authentication;
using UML.Core.Authentication.Microsoft;
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

        var select = new Command("select", "Select the current profile");
        var selectTarget = new Argument<string>("target") { Description = "Profile UUID, account id or username" };
        select.Arguments.Add(selectTarget);
        select.SetAction(async (parseResult, cancellationToken) =>
            await SelectAsync(parseResult.GetValue(selectTarget)!, cancellationToken));

        var list = new Command("list", "List profiles");
        list.SetAction(_ => ListProfiles());

        profile.Subcommands.Add(add);
        profile.Subcommands.Add(remove);
        profile.Subcommands.Add(select);
        profile.Subcommands.Add(list);

        var exit = new Command("exit", "Exit the CLI");
        exit.Aliases.Add("quit");
        exit.SetAction(_ => _exitRequested = true);

        root.Subcommands.Add(profile);
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
            Error(exception.Message);
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
