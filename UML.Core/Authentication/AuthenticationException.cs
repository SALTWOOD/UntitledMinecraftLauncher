// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Authentication;

public sealed class AuthenticationException : Exception
{
    public AuthenticationException(string error, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public string Error { get; }
}
