// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using UML.Core.Authentication.Microsoft;
using UML.Core.Configuration;

namespace UML.Core.Profile;

public sealed class ProfileManager
{
    private static readonly ProfileManager Shared = new();

    private readonly Lock _gate = new();

    public static ProfileManager Current => Shared;

    public event EventHandler<ProfileChangedEventArgs>? Changed;

    public IReadOnlyList<UserProfile> Profiles => Config.Profiles;

    public UserProfile? CurrentProfile
    {
        get
        {
            var uuid = Config.SelectedProfileUuid;
            return uuid is null ? null : Find(uuid.Value);
        }
    }

    public UserProfile? Find(Guid uuid)
    {
        var profiles = Config.Profiles;
        var index = IndexOf(profiles, uuid);
        return index < 0 ? null : profiles[index];
    }

    public UserProfile Add(UserProfile profile, bool select = false)
    {
        ArgumentNullException.ThrowIfNull(profile);

        lock (_gate)
        {
            var uuid = GetUuid(profile);
            var profiles = Config.Profiles;
            if (IndexOf(profiles, uuid) >= 0)
                throw new InvalidOperationException($"A profile with UUID {uuid} already exists.");

            Config.Profiles = profiles.Add(profile);
            if (select) Config.SelectedProfileUuid = uuid;
        }

        Raise(ProfileChangeKind.Added, profile);
        return profile;
    }

    public MicrosoftProfile Add(MicrosoftAccount account, bool select = true)
    {
        ArgumentNullException.ThrowIfNull(account);

        var profile = new MicrosoftProfile(
            account.AccountId,
            account.Username,
            account.Uuid,
            account.UserType,
            account.Tokens,
            account.SkinUrl,
            account.SkinVariant,
            DateTimeOffset.UtcNow);

        return (MicrosoftProfile)Upsert(profile, select);
    }

    public OfflineProfile AddOffline(string username, Guid? uuid = null, bool select = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        var name = username.Trim();
        var profile = new OfflineProfile(name, uuid ?? CreateOfflineUuid(name));
        return (OfflineProfile)Upsert(profile, select);
    }

    public UserProfile Upsert(UserProfile profile, bool select = true)
    {
        ArgumentNullException.ThrowIfNull(profile);

        UserProfile? previous = null;
        ProfileChangeKind kind;

        lock (_gate)
        {
            var uuid = GetUuid(profile);
            var profiles = Config.Profiles;
            var index = IndexOf(profiles, uuid);

            if (index < 0)
            {
                Config.Profiles = profiles.Add(profile);
                kind = ProfileChangeKind.Added;
            }
            else
            {
                previous = profiles[index];
                Config.Profiles = profiles.SetItem(index, profile);
                kind = ProfileChangeKind.Updated;
            }

            if (select) Config.SelectedProfileUuid = uuid;
        }

        Raise(kind, profile, previous);
        return profile;
    }

    public bool Update(UserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        UserProfile? previous = null;

        lock (_gate)
        {
            var profiles = Config.Profiles;
            var index = IndexOf(profiles, GetUuid(profile));
            if (index < 0) return false;

            previous = profiles[index];
            Config.Profiles = profiles.SetItem(index, profile);
        }

        Raise(ProfileChangeKind.Updated, profile, previous);
        return true;
    }

    public bool Remove(Guid uuid)
    {
        UserProfile? removed;

        lock (_gate)
        {
            var profiles = Config.Profiles;
            var index = IndexOf(profiles, uuid);
            if (index < 0) return false;

            removed = profiles[index];
            Config.Profiles = profiles.RemoveAt(index);
            if (Config.SelectedProfileUuid == uuid)
                Config.SelectedProfileUuid = null;
        }

        Raise(ProfileChangeKind.Removed, removed);
        return true;
    }

    public bool Remove(UserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Remove(GetUuid(profile));
    }

    public bool Select(Guid? uuid)
    {
        if (uuid is null)
        {
            lock (_gate) Config.SelectedProfileUuid = null;

            Raise(ProfileChangeKind.Cleared, null);
            return true;
        }

        UserProfile selected;

        lock (_gate)
        {
            var profiles = Config.Profiles;
            var index = IndexOf(profiles, uuid.Value);
            if (index < 0) return false;

            selected = profiles[index];
            if (selected is MicrosoftProfile microsoft)
            {
                selected = microsoft with { LastUsedAt = DateTimeOffset.UtcNow };
                Config.Profiles = profiles.SetItem(index, selected);
            }

            Config.SelectedProfileUuid = uuid;
        }

        Raise(ProfileChangeKind.Selected, selected);
        return true;
    }

    public bool Select(UserProfile? profile)
    {
        if (profile is null) return Select((Guid?)null);
        return Select(GetUuid(profile));
    }

    public void Clear()
    {
        lock (_gate)
        {
            Config.Profiles = [];
            Config.SelectedProfileUuid = null;
        }

        Raise(ProfileChangeKind.Cleared, null);
    }

    public ValueTask SaveAsync(CancellationToken cancellationToken = default) =>
        ConfigService.Current.FlushAsync(cancellationToken);

    public static Guid CreateOfflineUuid(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        Span<byte> hash = stackalloc byte[16];
        MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username), hash);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        return new Guid(hash);
    }

    private static Guid GetUuid(UserProfile profile) =>
        profile switch
        {
            MicrosoftProfile microsoft => microsoft.Uuid,
            OfflineProfile offline => offline.Uuid,
            _ => throw new ArgumentException($"Unknown profile type {profile.GetType()}.", nameof(profile))
        };

    private static int IndexOf(ImmutableArray<UserProfile> profiles, Guid uuid)
    {
        for (var index = 0; index < profiles.Length; index++)
            if (GetUuid(profiles[index]) == uuid)
                return index;

        return -1;
    }

    private void Raise(ProfileChangeKind kind, UserProfile? profile, UserProfile? previous = null) =>
        Changed?.Invoke(this, new ProfileChangedEventArgs(kind, profile, previous));
}
