using WorldTime.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime;

namespace WorldTime.BackgroundServices;
/// <summary>
/// Selectively fills the user cache without overwhelming memory, database, or network resources.
/// </summary>
class AutoUserDownload : BackgroundService {
    private static readonly SemaphoreSlim GCHoldSemaphore = new(1, 1);
    private const long MemoryBudget = 200_000_000;

    private readonly HashSet<ulong> _skippedGuilds = [];

    public AutoUserDownload(ShardInstance instance) : base(instance)
        => Shard.DiscordClient.Disconnected += OnDisconnect;
    ~AutoUserDownload() => Shard.DiscordClient.Disconnected -= OnDisconnect;

    private Task OnDisconnect(Exception ex) {
        _skippedGuilds.Clear();
        return Task.CompletedTask;
    }

    public override async Task OnTick(int tickCount, CancellationToken token) {
        await GCHoldSemaphore.WaitAsync(token).ConfigureAwait(false);
        int processed;
        try {
            var mustFetch = DetermineDownloadableGuilds();
            processed = await DoDownloadingAsync(mustFetch, token).ConfigureAwait(false);
        } finally {
            GCHoldSemaphore.Release();
        }
        if (processed > 0) Log($"Member list downloads handled for {processed} guilds.");

        // but we're -still- running into memory constraints... let's run it just this once?
        // given the new logic, this should only run once, on the first tick.
        
        if (processed > 100) {
            Log("Performing post-cleanup...");
            var before = GC.GetTotalMemory(forceFullCollection: false);
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            var after = GC.GetTotalMemory(forceFullCollection: true);
            Log($"Done - Reclaimed {before - after:N0} bytes.");
            Log("tick count " + tickCount);
        }
    }

    // Consider guilds with incomplete member lists that have not previously had failed downloads,
    // and where user-specific configuration exists.
    private HashSet<ulong> DetermineDownloadableGuilds() {
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

    private async Task<int> DoDownloadingAsync(HashSet<ulong> mustFetch, CancellationToken token) {
        var processed = 0;
        foreach (var item in mustFetch) {
            // We're useless if not connected
            if (Shard.DiscordClient.ConnectionState != ConnectionState.Connected) break;

            /*
                Whether due to the Discord library or this code or even both, this method
                results in a sort of memory leak. This code causes heap allocations that
                according to the garbage collector are beyond ephemeral, thus getting promoted
                to gen 1 or 2 and retained indefinitely by the runtime. This overhead is small
                for a single guild, but it adds up massively if the process is running even a
                handful of shards with thousands of guilds in total.

                Unfortunately, manually invoking the GC is not an option when trying to work
                within a limited amount of physical RAM. Constant full garbage collection
                results in frequent paging on the OS side and interferes with swapping and
                memory compression systems, starving the host of physical memory.
                The result is not pretty when there are 10 or more processes each handling
                tens of thousands of guilds.

                Thus the rationale for establishing these NoGCRegions. This unfortunately
                introduces a (small) bottleneck, with individual shard downloads no longer able
                to run in parallel. Solving this requires refactoring or a clever new solution.
                But as of this writing, saving memory is the biggest priority right now.
            */
            GC.TryStartNoGCRegion(MemoryBudget);
            SocketGuild? guild = null;
            try {
                guild = Shard.DiscordClient.GetGuild(item);
                if (guild == null) continue; // Guild disappeared between filtering and now
                
                var dl = guild.DownloadUsersAsync();
                try {
                    dl.Wait(20_000, token); // Wait no more than 20 seconds
                } catch (Exception) { }
                if (token.IsCancellationRequested) return processed; // Skip all reporting, error logging on cancellation

                if (dl.IsFaulted) {
                    Log("Exception thrown by download task: " + dl.Exception);
                    break;
                } else if (!dl.IsCompletedSuccessfully) {
                    Log($"Task unresponsive, will skip monitoring (G: {guild.Id}, U: {guild.MemberCount}).");
                    _skippedGuilds.Add(guild.Id);
                    continue;
                }
            } finally {
                if (GCSettings.LatencyMode == GCLatencyMode.NoGCRegion) {
                    GC.EndNoGCRegion();
                } else {
                    Log($"Note: NoGCRegion ended prematurely" + (guild == null ? "." : $"(G: {guild.Id}, U: {guild.MemberCount})."));
                }
            }
            processed++;
            if (token.IsCancellationRequested) return processed;

            // Allow other tasks to run in the meantime
            //await Task.Yield();
            
        }
        return processed;
    }
}
