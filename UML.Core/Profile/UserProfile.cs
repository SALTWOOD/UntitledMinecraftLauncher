// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Serialization;

namespace UML.Core.Profile;

[JsonPolymorphic(InferClosedTypePolymorphism = true)]
public closed record class UserProfile; // C# 15 is right!