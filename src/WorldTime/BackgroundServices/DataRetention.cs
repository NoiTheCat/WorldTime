using System.Text;
using Microsoft.EntityFrameworkCore;
using WorldTime.Data;

namespace WorldTime.BackgroundServices;
class DataRetention : BackgroundService {
    private readonly int ProcessInterval;

    // Amount of days without updates before data is considered stale and up for deletion.
    const int StaleUserThreashold = 90;

    public DataRetention(ShardInstance instance) : base(instance)
        => ProcessInterval = 21600 / Shard.Config.BackgroundInterval; // Process about once per six hours

    public override async Task OnTick(int tickCount, CancellationToken token) {
        // Run only a subset of shards each time, each running every ProcessInterval ticks.
        if ((tickCount + Shard.ShardId) % ProcessInterval != 0) return;

        await DatabaseAccessSemaphore.WaitAsync(token);
        try {
            await RemoveStaleEntriesAsync(token);
        } finally {
            try {
                DatabaseAccessSemaphore.Release();
            } catch (ObjectDisposedException) { }
        }
    }

    private async Task RemoveStaleEntriesAsync(CancellationToken token) {
        var opts = new DbContextOptionsBuilder<BotDatabaseContext>();
        ShardManager.BuildSqlOptions(opts);
        using var db = new BotDatabaseContext(opts.Options);

        // Update guild users
        var now = DateTimeOffset.UtcNow;
#if DEBUG
        await db.UserEntries.ExecuteUpdateAsync(upd => upd.SetProperty(p => p.LastSeen, now), token);
        Log("DEBUG: Extended TTL of existing entries.");
        return;
#pragma warning disable CS0162 // Unreachable code detected
#endif
        var updatedUsers = 0;
        foreach (var guild in Shard.DiscordClient.Guilds) {
            var local = Shard.Cache.GetEntriesForGuild(guild.Id, false)
                .Select(e => e.UserId).ToList();

            foreach (var queue in local.Chunk(1000)) {
                updatedUsers += await db.UserEntries
                    .Where(gu => gu.GuildId == guild.Id)
                    .Where(gu => local.Contains(gu.UserId))
                    .ExecuteUpdateAsync(upd => upd.SetProperty(p => p.LastSeen, now), token);
            }
        }

        // And let go of old data
        var staleUserCount = await db.UserEntries
            .Where(gu => now - TimeSpan.FromDays(StaleUserThreashold) > gu.LastSeen)
            .ExecuteDeleteAsync(token);

        // Build report
        var resultText = new StringBuilder();
        resultText.Append($"Refreshed {updatedUsers} users.");
        if (staleUserCount != 0) resultText.Append($" Discarded {staleUserCount} users.");
        Log(resultText.ToString());
    }
}
