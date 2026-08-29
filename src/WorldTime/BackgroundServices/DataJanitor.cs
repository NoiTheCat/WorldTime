using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NoiPublicBot;
using NoiPublicBot.BackgroundServices;
using NoiPublicBot.Common.UserCache;
using WorldTime.Data;

namespace WorldTime.BackgroundServices;

// Keeps track of known existing users. Removes old unused data
sealed class DataJanitor : BackgroundService {
    private readonly int ProcessInterval;
    private static readonly SemaphoreSlim _dbGate = new(3);

    // Amount of days without updates before data is considered stale and up for deletion.
    const int StaleUserThreashold = 90;

    public DataJanitor()
        => ProcessInterval = 10_800 / Instance.UserConfig.BackgroundInterval; // Process about once every two hours

    public override async Task OnTick(int tickCount, CancellationToken token) {
        if (tickCount % ProcessInterval != 0) return;

        await _dbGate.WaitAsync(token);
        try {
#if DEBUG
            await DebugBumpAsync(token);
            return;
#pragma warning disable CS0162
#endif
            await RemoveStaleEntriesAsync(token);
        } finally {
            try {
                _dbGate.Release();
            } catch (ObjectDisposedException) { }
        }
    }

    private async Task RemoveStaleEntriesAsync(CancellationToken token) {
        using var db = BotDatabaseContext.New();

        // Update guild users
        var now = SystemClock.Instance.GetCurrentInstant();
        var cache = Shard.LocalServices.GetRequiredService<UserCache<BotDatabaseContext>>();
        var updatedUsers = 0;
        foreach (var guild in Shard.DiscordClient.Guilds) {
            var local = cache.GetGuild(guild.Id, false)?.Keys;
            if (local == null) continue;

            foreach (var queue in local.Chunk(1000))
            {
                updatedUsers += await db.UserEntries
                    .Where(gu => gu.GuildId == guild.Id)
                    .Where(gu => local.Contains(gu.UserId))
                    .ExecuteUpdateAsync(upd => upd.SetProperty(p => p.LastSeen, now), token).ConfigureAwait(false);
            }
        }
        Log($"Refreshed {updatedUsers} users.");

        // And let go of old data
        var staleUserCount = await db.UserEntries
            .Where(gu => now - Duration.FromDays(StaleUserThreashold) > gu.LastSeen)
            .ExecuteDeleteAsync(token);
        if (staleUserCount != 0) Log($"Discarded {staleUserCount} users across the whole database.");
    }

#if DEBUG
    private async Task DebugBumpAsync(CancellationToken token) {
        using var db = BotDatabaseContext.New();
        var now = SystemClock.Instance.GetCurrentInstant();
        await db.UserEntries.ExecuteUpdateAsync(upd => upd.SetProperty(p => p.LastSeen, now), token);
        Log("DEBUG: Extended TTL of existing entries.");
    }
#endif
}
