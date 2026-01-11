using Microsoft.EntityFrameworkCore;
using WorldTime.Data;

namespace WorldTime.BackgroundServices;
/// <summary>
/// Selectively fills and refreshes the locally-managed user cache on a periodic basis.
/// </summary>
class AutoUserDownload(ShardInstance instance) : BackgroundService(instance) {
    private static readonly SemaphoreSlim _dlGate = new(3);

    public override async Task OnTick(int tickCount, CancellationToken token) {
        // TODO logic for per-shard staggering (maybe take from bb->retention)

        var missingFromCache = CreateDownloadList();
        _ = await ExecDownloadListAsync(mustFetch, token).ConfigureAwait(false);
    }

    // Consider guild users that have existing configuration but are not in our cache.
    private Dictionary<ulong, List<ulong>> CreateDownloadList() {
        using var db = new BotDatabaseContext(new DbContextOptionsBuilder<BotDatabaseContext>()
            .UseNpgsql(Program.SqlConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

        var guilds = Shard.DiscordClient.Guilds.Select(g => g.Id);

        var dbUsers = db.UserEntries.AsNoTracking()
            .Where(u => guilds.Contains(u.GuildId))
            .Select(v => new { v.GuildId, v.UserId })
            .GroupBy(g => g.GuildId)
            .ToDictionary(k => k.Key, v => v.Select(g => g.UserId).ToList());

        var result = new Dictionary<ulong, List<ulong>>();
        foreach (var (guild, dbUserEntries) in dbUsers) {
            if (!Shard.Cache.TryGetGuildUsers(guild, out var inCache)) {
                // our cache is empty - fetch them all
                result[guild] = dbUserEntries;
            } else {
                result[guild] = [.. dbUserEntries.Except(inCache)];
            }
        }
        return result;
    }

    private async Task<int> ExecDownloadListAsync(HashSet<ulong> mustFetch, CancellationToken token) {
        var processed = 0;
        foreach (var item in mustFetch) {
            await _dlGate.WaitAsync(token).ConfigureAwait(false);
            try {
                // We're useless if not connected
                if (Shard.DiscordClient.ConnectionState != ConnectionState.Connected) break;

                SocketGuild? guild = null;
                guild = Shard.DiscordClient.GetGuild(item);
                if (guild == null) continue; // Guild disappeared between filtering and now
                if (guild.HasAllMembers) continue; // Download likely already invoked by user input

                var dl = guild.DownloadUsersAsync();
                if (await Task.WhenAny(dl, Task.Delay(30_000, token)) != dl) {
                    if (!dl.IsCompletedSuccessfully) {
                        Log($"Task taking too long, will skip monitoring (G: {guild.Id}, U: {guild.MemberCount}).");
                        _skippedGuilds.Add(guild.Id);
                        continue;
                    }
                }
                if (dl.IsFaulted) {
                    Log("Exception thrown by download task: " + dl.Exception);
                    break;
                }
            } finally {
                _dlGate.Release();
            }
            processed++;
            ConsiderGC();
            if (token.IsCancellationRequested) break;

            // This loop can last a very long time on startup.
            // Avoid starving other tasks.
            await Task.Yield();
        }
        return processed;
    }
}
