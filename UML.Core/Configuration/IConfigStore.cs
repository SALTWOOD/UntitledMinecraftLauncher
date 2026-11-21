// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace UML.Core.Configuration;

public interface IConfigStore
{
    ValueTask<ConfigDocument> LoadAsync(CancellationToken cancellationToken = default);
    ValueTask SaveAsync(ConfigDocument document, CancellationToken cancellationToken = default);
}
