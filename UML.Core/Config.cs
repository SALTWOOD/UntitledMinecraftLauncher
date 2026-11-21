// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Immutable;
using UML.Core.Configuration;
using UML.Core.Profile;

namespace UML.Core;

public static partial class Config
{
    public static readonly ConfigKey<ImmutableArray<UserProfile>> ProfilesKey =
        new ConfigKey<ImmutableArray<UserProfile>>("profiles", [], ConfigLayer.Local);

    public static ImmutableArray<UserProfile> Profiles
    {
        get => ConfigService.Current.Get(ProfilesKey);
        set => ConfigService.Current.Set(ProfilesKey, value);
    }

    public static readonly ConfigKey<Guid?> SelectedProfileKey =
        new ConfigKey<Guid?>("profile.selected", null, ConfigLayer.Local);

    public static Guid? SelectedProfileUuid
    {
        get => ConfigService.Current.Get(SelectedProfileKey);
        set => ConfigService.Current.Set(SelectedProfileKey, value);
    }
}