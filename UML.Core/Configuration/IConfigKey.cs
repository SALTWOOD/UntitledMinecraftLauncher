// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Configuration;

public interface IConfigKey
{
    string Name { get; }
    ConfigLayer Layer { get; }
    Type ValueType { get; }
    object? DefaultValueObject { get; }
}
