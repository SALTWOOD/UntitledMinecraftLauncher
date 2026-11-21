// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Configuration;

public class ConfigException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class ConfigScopeException(string message) : ConfigException(message);

public sealed class ConfigStoreException(string message, Exception? innerException = null) : ConfigException(message, innerException);
