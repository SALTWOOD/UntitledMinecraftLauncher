// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Configuration;

public readonly record struct ConfigChange<T>(ConfigKey<T> Key, T OldValue, T NewValue, ConfigContext Context);
