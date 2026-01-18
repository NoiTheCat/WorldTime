using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using WorldTime.Data;

namespace WorldTime.Caching;

// Handles requests for refreshing guild caches. To avoid duplicate work, ongoing jobs are tracked here.
// If any duplicate requests arrive, they are given the appropriate ongoing fetch task.
class Coordinator(ShardInstance parent) {
    // Discord limits to 50 requests per second per connection for all communications, not just this.
    // Tune as needed. This value always stays hardcoded.
    const int MaxConcurrentRequests = 12;

    // Time to delay sending out a request, in milliseconds. Consider chunk size when adjusting.
    const int JitterMin = 100;
    const int JitterMax = 2000;
    const int RequestBatchSize = 50;

    private static readonly SemaphoreSlim _downloadGate = new(MaxConcurrentRequests);

    // Dictionary of guild ID -> lazy task of RefreshInternal
    private readonly ConcurrentDictionary<ulong, Lazy<Task>> _runners = new();

    private ShardInstance Shard { get; } = parent;
    private DiscordSocketClient Client => Shard.DiscordClient;
    private UserCache Cache => Shard.Cache;

    public Task RequestGuildRefreshAsync(BotDatabaseContext ctx, ulong guildId) {
        var missing = GetCacheMissingUsers(ctx, guildId);
        if (missing.Count == 0) return Task.CompletedTask;
        return _runners.GetOrAdd(guildId, new Lazy<Task>(() =>
            RefreshInternalAsync(guildId, missing, CancellationToken.None),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    // Guild-specific search. Returns all IDs in database not also in current cache.
    private List<ulong> GetCacheMissingUsers(BotDatabaseContext context, ulong guildId) {
        var local = Cache.GetEntriesForGuild(guildId, true)
            .Select(e => e.UserId)
            .ToList();
        var remote = context.UserEntries
            .Where(e => e.GuildId == guildId)
            .Select(e => e.UserId)
            .ToList();
        return [.. remote.Except(local)];
    }

    #region Specific to background task
    // Directly called by background task. Not at all useful to anyone else.
    public Task BackgroundRefreshShardTask(CancellationToken token) {
        var missing = BuildShardDownloadList();
        var enqueued = _runners.Keys.ToHashSet();
        var bgRunners = new List<Task>();

        foreach (var (guildId, users) in missing) {
            if (Shard.DiscordClient.GetGuild(guildId) is null) continue; // skip pointless work
            if (enqueued.Contains(guildId)) continue; // ignore tasks already running
            if (users.Count == 0) continue; // this is very possible

            var newtask = _runners.GetOrAdd(guildId,
                new Lazy<Task>(() => RefreshInternalAsync(guildId, users, token),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
            bgRunners.Add(newtask);
        }
        return Task.WhenAll(bgRunners);
    }

    // Like GetCacheMissingUsers, but in one query
    private Dictionary<ulong, List<ulong>> BuildShardDownloadList() {
        var opts = new DbContextOptionsBuilder<BotDatabaseContext>();
        ShardManager.BuildSqlOptions(opts);
        using var db = new BotDatabaseContext(opts.Options);

        var guilds = Client.Guilds.Select(g => g.Id);

        var dbUsers = db.UserEntries.AsNoTracking()
            .Where(u => guilds.Contains(u.GuildId))
            .Select(v => new { v.GuildId, v.UserId })
            .GroupBy(g => g.GuildId)
            .ToDictionary(k => k.Key, v => v.Select(g => g.UserId).ToList());

        var result = new Dictionary<ulong, List<ulong>>();
        foreach (var (guild, remoteEntries) in dbUsers) {
            // Including null entries; backing off on retrying missing entries until they expire
            var localEntries = Cache.GetEntriesForGuild(guild, true).Select(e => e.UserId);
            result[guild] = [.. remoteEntries.Except(localEntries)];
        }
        return result;
    }
    #endregion

    // Takes a guild/user list and runs it in batches until done or cancelled.
    // This returns the task for all guild requests to be awaited on.
    private async Task RefreshInternalAsync(ulong guildId, IEnumerable<ulong> users, CancellationToken token) {
        try {
            foreach (var chunk in users.Chunk(RequestBatchSize)) {
                if (Shard.DiscordClient.ConnectionState != ConnectionState.Connected) break;
                await RetrieveGuildUserBatchAsync(guildId, chunk, token).ConfigureAwait(false);
                await Task.Yield();
                if (token.IsCancellationRequested) return;
            }
            Cache.Sweep(guildId);
        } finally {
Console.WriteLine("refresh end   " + guildId);
            _runners.TryRemove(guildId, out _);
        }
    }

    // Assumes caller has already organized users in batches
    private Task RetrieveGuildUserBatchAsync(ulong g, IReadOnlyList<ulong> users, CancellationToken token) {
        var tasks = users.Select(async u => {
            await _downloadGate.WaitAsync(token).ConfigureAwait(false);
            try {
                await Task.Delay(Program.JitterSource.Value!.Next(JitterMin, JitterMax)).ConfigureAwait(false);

                var incoming = await Shard.DiscordClient.Rest.GetGuildUserAsync(
                    g, u, new RequestOptions { CancelToken = token }).ConfigureAwait(false);
                if (incoming is not null) {
                    Shard.Cache.Update(UserInfo.CreateFrom(incoming));
                } else {
                    Shard.Cache.Update(UserInfo.NullFrom(g, u));
                }
            } catch {
                // TODO exception handling for temporary network issues
            } finally {
                _downloadGate.Release();
            }
        });
        return Task.WhenAll(tasks);
    }
}