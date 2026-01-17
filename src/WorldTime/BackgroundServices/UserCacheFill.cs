namespace WorldTime.BackgroundServices;

// Formerly AutoUserDownload, but now Coordinator does all of the work.
// Particularly useful during startup where lag may cause a backup of duplicate requests.
class UserCacheFill(ShardInstance instance) : BackgroundService(instance) {
    public override Task OnTick(int tickCount, CancellationToken token)
        => Shard.Fetcher.BackgroundRefreshShardTask(token);
}
