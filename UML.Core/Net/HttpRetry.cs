// SPDX-FileCopyrightText: 2026 SALTWOOD
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Net;

namespace UML.Core.Net;

public static class HttpRetry
{
    public static TimeSpan BaseDelay { get; } = TimeSpan.FromSeconds(1);

    public static TimeSpan MaxDelay { get; } = TimeSpan.FromSeconds(30);

    private static readonly HashSet<HttpStatusCode> RetryableStatusCodes =
    [
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    ];

    public static TimeSpan ExponentialDelay(int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);

        var milliseconds = Math.Min(
            BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1),
            MaxDelay.TotalMilliseconds);

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        int retries = 3,
        Func<int, TimeSpan>? delayFactory = null,
        Func<Exception, bool>? shouldRetry = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentOutOfRangeException.ThrowIfNegative(retries);

        delayFactory ??= ExponentialDelay;
        shouldRetry ??= exception => IsTransient(exception, cancellationToken);

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (attempt < retries && shouldRetry(exception))
            {
                await Task.Delay(delayFactory(attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        int retries = 3,
        Func<int, TimeSpan>? delayFactory = null,
        Func<HttpResponseMessage, bool>? shouldRetry = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(requestFactory);
        ArgumentOutOfRangeException.ThrowIfNegative(retries);

        delayFactory ??= ExponentialDelay;
        shouldRetry ??= static response => RetryableStatusCodes.Contains(response.StatusCode);

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HttpResponseMessage? response = null;

            try
            {
                using var request = requestFactory();
                response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (attempt >= retries || !shouldRetry(response))
                    return response;

                var delay = ClampDelay(GetRetryAfter(response) ?? delayFactory(attempt + 1));
                response.Dispose();
                response = null;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (attempt < retries && IsTransient(exception, cancellationToken))
            {
                response?.Dispose();
                await Task.Delay(delayFactory(attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or IOException or TimeoutException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is not { } retryAfter) return null;
        if (retryAfter.Delta is { } delta) return delta;
        if (retryAfter.Date is { } date) return date - DateTimeOffset.UtcNow;
        return null;
    }

    private static TimeSpan ClampDelay(TimeSpan delay) =>
        delay <= TimeSpan.Zero ? TimeSpan.Zero
        : delay > MaxDelay ? MaxDelay
        : delay;
}
