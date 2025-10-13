using WorldTime.Data;
using Microsoft.EntityFrameworkCore;

namespace WorldTime.BackgroundServices;
/// <summary>
/// Selectively fills the user cache without overwhelming memory, database, or network resources.
/// </summary>
class AutoUserDownload : BackgroundService {
    // experimental: have only one shard downloading at a time
    // could consider raising this limit later
    private static readonly SemaphoreSlim DLHoldSemaphore = new(1, 1);
    private const long MemoryBudget = 200_000_000;

    private readonly HashSet<ulong> _skippedGuilds = [];

    public AutoUserDownload(ShardInstance instance) : base(instance)
        => Shard.DiscordClient.Disconnected += OnDisconnect;
    ~AutoUserDownload()
        => Shard.DiscordClient.Disconnected -= OnDisconnect;

    private Task OnDisconnect(Exception ex) {
        _skippedGuilds.Clear();
        return Task.CompletedTask;
    }

    public override async Task OnTick(int tickCount, CancellationToken token) {
        await DLHoldSemaphore.WaitAsync(token).ConfigureAwait(false);
        int processed;
        try {
            var mustFetch = CreateDownloadList();
            processed = await ExecDownloadListAsync(mustFetch, token).ConfigureAwait(false);
        } finally {
            DLHoldSemaphore.Release();
        }

        if (processed > 50) {
            Log($"Handled {processed} guilds. Performing post-cleanup...");
            var before = GC.GetTotalMemory(forceFullCollection: false);
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            var after = GC.GetTotalMemory(forceFullCollection: true);
            Log($"Done. Reclaimed {before - after:N0} bytes.");
        }
    }

    // Consider guilds with incomplete member lists that have not previously had failed downloads,
    // and where user-specific configuration exists.
    private HashSet<ulong> CreateDownloadList() {
        var incompleteCaches = Shard.DiscordClient.Guilds
                .Where(g => !g.HasAllMembers)               // Consider guilds with incomplete caches,
                .Where(g => !_skippedGuilds.Contains(g.Id)) // that have not previously failed during this connection, and...
                .Select(g => g.Id)
                .ToHashSet();
        using var db = new BotDatabaseContext();            // ...where some user data exists.
        return [.. db.UserEntries.AsNoTracking()
                                 .Where(e => incompleteCaches.Contains(e.GuildId))
                                 .Select(e => e.GuildId)
                                 .Distinct()];
    }

    private async Task<int> ExecDownloadListAsync(HashSet<ulong> mustFetch, CancellationToken token) {
        var processed = 0;
        foreach (var item in mustFetch) {
            // We're useless if not connected
            if (Shard.DiscordClient.ConnectionState != ConnectionState.Connected) break;

            SocketGuild? guild = null;
            guild = Shard.DiscordClient.GetGuild(item);
            if (guild == null) continue; // Guild disappeared between filtering and now
            if (guild.HasAllMembers) continue; // Download likely already invoked by user input

            var dl = guild.DownloadUsersAsync();
            try {
                dl.Wait(20_000, token); // Wait no more than 20 seconds
            } catch (Exception) { }
            if (token.IsCancellationRequested) return processed; // Skip all reporting if cancel

            if (dl.IsFaulted) {
                Log("Exception thrown by download task: " + dl.Exception);
                break;
            } else if (!dl.IsCompletedSuccessfully) {
                Log($"Task unresponsive, will skip monitoring (G: {guild.Id}, U: {guild.MemberCount}).");
                _skippedGuilds.Add(guild.Id);
                continue;
            }
            processed++;
            if (token.IsCancellationRequested) return processed;

            if (processed % 250 == 0 && processed > 0) {
                // Take a break - keep this from starving other tasks
                await Task.Yield();
            }
        }
        return processed;
    }
}
