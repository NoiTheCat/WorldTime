using Microsoft.EntityFrameworkCore;
using WorldTime.Caching;
using WorldTime.Data;

namespace WorldTime.BackgroundServices;
/// <summary>
/// Selectively fills and refreshes the locally-managed user cache on a periodic basis.
/// </summary>
class UserCacheFill(ShardInstance instance) : BackgroundService(instance) {
    // Discord limits to 50 requests per second per connection for all communications, not just this.
    // Tune as needed. This value always stays hardcoded.
    private static readonly SemaphoreSlim _downloadGate = new(18);

    // Time to delay sending out a request, in milliseconds. Consider chunk size when adjusting.
    const int JitterMin = 100;
    const int JitterMax = 1000;
    const int RequestChunkSize = 50;

    public override async Task OnTick(int tickCount, CancellationToken token) {
        var missingFromCache = BuildShardDownloadList();

        foreach (var (guildId, users) in missingFromCache) {
            var guild = Shard.DiscordClient.GetGuild(guildId);
            if (guild is null) continue;
            foreach (var chunk in users.Chunk(RequestChunkSize)) {
                await RetrieveGuildUserBatchAsync(guild, chunk, token);
                token.ThrowIfCancellationRequested();
                await Task.Yield();
            }
            Shard.Cache.Sweep(guild.Id);
        }
    }

    // Consider guild users that have existing configuration but are not in our cache.
    // Considers all guilds in this shard at once.
    private Dictionary<ulong, List<ulong>> BuildShardDownloadList() {
        var opts = new DbContextOptionsBuilder<BotDatabaseContext>();
        ShardManager.BuildSqlOptions(opts);
        using var db = new BotDatabaseContext(opts.Options);

        var guilds = Shard.DiscordClient.Guilds.Select(g => g.Id);

        var dbUsers = db.UserEntries.AsNoTracking()
            .Where(u => guilds.Contains(u.GuildId))
            .Select(v => new { v.GuildId, v.UserId })
            .GroupBy(g => g.GuildId)
            .ToDictionary(k => k.Key, v => v.Select(g => g.UserId).ToList());

        var result = new Dictionary<ulong, List<ulong>>();
        foreach (var (guild, dbUserEntries) in dbUsers) {
            var inCache = Shard.Cache.GetExistingGuildUsers(guild, true);
            result[guild] = [.. dbUserEntries.Except(inCache)];
        }
        return result;
    }

    internal Task RetrieveGuildUserBatchAsync(SocketGuild g, IReadOnlyList<ulong> users, CancellationToken token) {
        var tasks = users.Select(async u => {
            await _downloadGate.WaitAsync(token);
            try {
                await Task.Delay(Program.JitterSource.Value!.Next(JitterMin, JitterMax));

                var incoming = await Shard.DiscordClient.Rest
                    .GetGuildUserAsync(g.Id, u, new RequestOptions { CancelToken = token });
                if (incoming is not null) {
                    Shard.Cache.Update(UserInfo.CreateFrom(incoming));
                } else {
                    Shard.Cache.Update(UserInfo.NullFrom(g.Id, u));
                }
            } finally {
                _downloadGate.Release();
            }
        });
        return Task.WhenAll(tasks);
    }
}
