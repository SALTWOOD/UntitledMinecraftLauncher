// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Profile;

public sealed record OfflineProfile(
    string Username,
    Guid Uuid
) : UserProfile;