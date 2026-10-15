using System.Collections.Concurrent;

namespace Vocab.Sdk.AspNetCore;

/// <summary>
/// Single-flight token refresh for signed-in sessions. auth.vo.cab rotates refresh tokens and treats a second use
/// of a rotated-away token as theft: it revokes the whole session. Two requests carrying the same cookie near
/// expiry must therefore share ONE refresh, and a request that still carries the old cookie a moment later must
/// get the same new tokens instead of replaying the old refresh token.
/// </summary>
internal sealed class VocabSessionRefresher(TimeProvider time)
{
    /// <summary>How long a finished refresh stays available to requests still carrying the old cookie.</summary>
    internal static readonly TimeSpan RetainFor = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Entry> _refreshes = new(StringComparer.Ordinal);

    /// <summary>
    /// Refreshes <paramref name="refreshToken"/> once, however many callers ask concurrently (or within
    /// <see cref="RetainFor"/> of the first). A failed refresh is shared with the callers in flight and then forgotten.
    /// </summary>
    public Task<VocabTokens> RefreshAsync(string refreshToken, VocabAuthClient auth)
    {
        Evict();
        var entry = _refreshes.GetOrAdd(refreshToken, key => new Entry(() => RunAsync(key, auth)));
        return entry.Task;
    }

    /// <summary>How many refreshes are in flight or retained (for tests and diagnostics).</summary>
    internal int Count => _refreshes.Count;

    private async Task<VocabTokens> RunAsync(string refreshToken, VocabAuthClient auth)
    {
        try
        {
            // Not tied to the first caller's request: the result serves every request sharing this refresh.
            var tokens = await auth.RefreshAsync(refreshToken, CancellationToken.None);
            if (_refreshes.TryGetValue(refreshToken, out var entry))
                entry.CompletedAt = time.GetUtcNow();
            return tokens;
        }
        catch
        {
            _refreshes.TryRemove(refreshToken, out _);
            throw;
        }
    }

    private void Evict()
    {
        var cutoff = time.GetUtcNow() - RetainFor;
        foreach (var pair in _refreshes)
            if (pair.Value.CompletedAt is { } done && done < cutoff)
                _refreshes.TryRemove(pair.Key, out _);
    }

    private sealed class Entry(Func<Task<VocabTokens>> run)
    {
        private readonly Lazy<Task<VocabTokens>> _task = new(run, LazyThreadSafetyMode.ExecutionAndPublication);

        public Task<VocabTokens> Task => _task.Value;

        public DateTimeOffset? CompletedAt { get; set; }
    }
}
