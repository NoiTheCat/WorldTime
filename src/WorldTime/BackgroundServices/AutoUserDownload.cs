using Microsoft.EntityFrameworkCore;
using WorldTime.Data;

namespace WorldTime.BackgroundServices;
/// <summary>
/// Selectively fills and refreshes the locally-managed user cache on a periodic basis.
/// </summary>
class AutoUserDownload(ShardInstance instance) : BackgroundService(instance) {
    // Assuming a limit of 50 requests per second, and that the bot's doing other things
    // at the same time as this executes.
    // Rate limits are handled by the library - this service just waits if one is encountered.
    private static readonly SemaphoreSlim _downloadGate = new(20);

    public override async Task OnTick(int tickCount, CancellationToken token) {
        Shard.Cache.Sweep();
        var missingFromCache = BuildShardDownloadList();

        foreach (var (guildId, users) in missingFromCache) {
            var guild = Shard.DiscordClient.GetGuild(guildId);
            if (guild is null) continue;
            foreach (var chunk in users.Chunk(500)) {
                await RetrieveGuildUserBatchAsync(guild, chunk, token);
                token.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }
    }

    // Consider guild users that have existing configuration but are not in our cache.
    // Considers all guilds in this shard at once.
    private Dictionary<ulong, List<ulong>> BuildShardDownloadList() {
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

    internal Task RetrieveGuildUserBatchAsync(SocketGuild g, IReadOnlyList<ulong> users, CancellationToken token) {
        var tasks = users.Select(async u => {
            await _downloadGate.WaitAsync(token);
            try {
                var incoming = await Shard.DiscordClient.Rest
                    .GetGuildUserAsync(g.Id, u, new RequestOptions { CancelToken = token });
                // incoming may be null.
                // if so, it's stale config. 
                // TODO how to deal with it?
                if (incoming is not null) {
                    Shard.Cache.Update(new Caching.UserInfo {
                        GuildId = incoming.GuildId,
                        UserId = incoming.Id,
                        Username = incoming.Username,
                        GlobalName = incoming.GlobalName,
                        GuildNickname = incoming.Nickname
                    });
                }
                await Task.Delay(100);
            } finally {
                _downloadGate.Release();
            }
        });
        return Task.WhenAll(tasks);
    }
}
