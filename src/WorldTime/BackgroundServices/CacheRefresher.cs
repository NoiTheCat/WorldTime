using Microsoft.Extensions.DependencyInjection;
using NoiPublicBot.BackgroundServices;
using WorldTime.Data;

namespace WorldTime.BackgroundServices;

// Replaces the old AutoUserDownload, working very closely with the cache coordinator class
// to gradually keep the user cache filled and refreshed in the background.
sealed class CacheRefresher : BackgroundService {
    private static readonly SemaphoreSlim _concurrentBackgroundRefresh = new(1);

    public override async Task OnTick(int tickCount, CancellationToken token) {
        var db = BotDatabaseContext.New();
        var cache = Shard.LocalServices.GetRequiredService<UserCache<BotDatabaseContext>>();
        await _concurrentBackgroundRefresh.WaitAsync(token);
        try {
            await cache.BackgroundRefreshWholeShardAsync(db, ModuleConfig.FilterAllMissing, token);
        } finally {
            _concurrentBackgroundRefresh.Release();
        }
    }
}
