﻿using WorldTime.Data;
using Microsoft.EntityFrameworkCore;

namespace WorldTime.BackgroundServices;
/// <summary>
/// Selectively fills the user cache without overwhelming memory, database, or network resources.
/// </summary>
class AutoUserDownload : BackgroundService {
    private static readonly SemaphoreSlim _dlGate = new(3);
    private const int GCCallThreshold = 300; // TODO make configurable or do further testing
    private static readonly SemaphoreSlim _gcGate = new(1);
    private static int _jobCount = 0;

    private readonly HashSet<ulong> _skippedGuilds = [];

    public AutoUserDownload(ShardInstance instance) : base(instance)
        => Shard.DiscordClient.Disconnected += OnDisconnect;

    private Task OnDisconnect(Exception ex) {
        _skippedGuilds.Clear();
        return Task.CompletedTask;
    }

    public override async Task OnTick(int tickCount, CancellationToken token) {
        
        int processed;
        try {
            var mustFetch = CreateDownloadList();
            processed = await ExecDownloadListAsync(mustFetch, token).ConfigureAwait(false);
        } finally {
            _dlGate.Release();
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

    // Manages manual invocation of garbage collector.
    // Consecutive calls to DownloadUsersAsync inevitably causes a lot of slightly-less-than-temporary items to be held by the CLR,
    // and this adds up with hundreds of thousands of users. Alternate methods have been explored, but this so far has proven to be
    // the most stable, reliable, and quickest of them.
    private void ConsiderGC() {
        if (Interlocked.Increment(ref _jobCount) > GCCallThreshold) {
            if (_gcGate.Wait(0)) { // prevents repeated calls across threads
                try {
                    var before = GC.GetTotalMemory(forceFullCollection: false);
                    GC.Collect(2, GCCollectionMode.Forced, true, true);
                    var after = GC.GetTotalMemory(forceFullCollection: true);
                    Log($"Threshold reached. GC reclaimed {before - after:N0} bytes.");
                    Interlocked.Exchange(ref _jobCount, 0);
                } finally {
                    _gcGate.Release();
                }
            }
        }
    }
}
