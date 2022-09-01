using Microsoft.Extensions.DependencyInjection;
using WorldTime.Data;

namespace WorldTime;
/// <summary>
/// Proactively fills the user cache for guilds in which any time zone configuration exists.
/// </summary>
/// <remarks>Modeled after BirthdayBot's similar feature.</remarks>
class BackgroundUserListLoad : IDisposable {
    private readonly IServiceProvider _services;
    private readonly Task _workerTask;
    private readonly CancellationTokenSource _workerCancel;

    public BackgroundUserListLoad(IServiceProvider services) {
        _services = services;
        _workerCancel = new();
        _workerTask = Task.Factory.StartNew(Worker, _workerCancel.Token,
                      TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void Dispose() {
        _workerCancel.Cancel();
        _workerCancel.Dispose();
        _workerTask.Dispose();
    }

    private async Task Worker() {
        while (!_workerCancel.IsCancellationRequested) {
            // Interval same as status
            await Task.Delay(WorldTime.StatusInterval * 1000, _workerCancel.Token);

            foreach (var shard in _services.GetRequiredService<DiscordShardedClient>().Shards) {
                try {
                    await ProcessShard(shard);
                } catch (Exception ex) {
                    Program.Log(nameof(BackgroundUserListLoad), ex.ToString());
                }
            }
        }
    }

    private async Task ProcessShard(DiscordSocketClient shard) {
        using var db = _services.GetRequiredService<BotDatabaseContext>();

        // Check when a guild's cache is incomplete...
        var incompleteCaches = shard.Guilds.Where(g => !g.HasAllMembers).Select(g => (long)g.Id).ToHashSet();
        // ...and contains any user data.
        var mustFetch = db.UserEntries.Where(e => incompleteCaches.Contains(e.GuildId)).Select(e => e.GuildId).Distinct();

        var processed = 0;
        foreach (var item in mustFetch) {
            // May cause a disconnect in certain situations. Cancel all further attempts until the next pass if it happens.
            if (shard.ConnectionState != ConnectionState.Connected) break;

            var guild = shard.GetGuild((ulong)item);
            if (guild == null) continue; // A guild disappeared...?
            await guild.DownloadUsersAsync().ConfigureAwait(false); // We're already on a seperate thread, no need to use Task.Run
            await Task.Delay(200, CancellationToken.None).ConfigureAwait(false); // Must delay, or else it seems to hang...
            processed++;
        }

        if (processed > 100) Program.Log(nameof(BackgroundUserListLoad), $"Explicit user list request processed for {processed} guilds.");
    }
}