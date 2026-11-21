// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Profile;

public enum ProfileChangeKind
{
    Added,
    Updated,
    Removed,
    Selected,
    Cleared
}

public sealed class ProfileChangedEventArgs(
    ProfileChangeKind kind,
    UserProfile? profile,
    UserProfile? previous = null) : EventArgs
{
    public ProfileChangeKind Kind { get; } = kind;
    public UserProfile? Profile { get; } = profile;
    public UserProfile? Previous { get; } = previous;
}
