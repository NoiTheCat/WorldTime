global using Discord;
global using Discord.WebSocket;
using System.Reflection;
using System.Text;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorldTime.Caching;
using WorldTime.Config;
using WorldTime.Data;

namespace WorldTime;

/// <summary>
/// More or less the main class for the program. Handles individual shards and provides frequent
/// status reports regarding the overall health of the application.
/// </summary>
class ShardManager : IDisposable {
    private readonly Dictionary<int, ShardInstance?> _shards;
    private readonly CancellationTokenSource _mainCancel = new();
    private readonly Task _statusTask;

    internal Configuration Config { get; }
    internal InteractionService Interactions { get; }

    public ShardManager(Configuration cfg) {
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Log($"World Time v{ver!.ToString(3)} is starting...");
        Config = cfg;

        // Early InteractionService init with dummy client and no global service provider
        Interactions = new(new DiscordSocketClient(), null);
        Interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), null).Wait();

        // Allocate shards based on configuration
        _shards = [];
        for (var i = Config.Sharding.StartId; i < (Config.Sharding.StartId + Config.Sharding.Amount); i++) {
            _shards.Add(i, null);
        }

        // Start status reporting task
        _statusTask = StatusLoop();
    }

    public void Dispose() {
        _mainCancel.Cancel();
        _statusTask.Wait(10000);
        if (!_statusTask.IsCompleted)
            Log("Warning: Main thread did not cleanly finish up in time. Continuing...");

        Log("Shutting down all shards...");
        var shardDisposes = new List<Task>();
        foreach (var item in _shards) {
            if (item.Value == null) continue;
            shardDisposes.Add(Task.Run(item.Value.Dispose));
        }
        if (!Task.WhenAll(shardDisposes).Wait(30000)) {
            Log("Warning: Not all shards terminated cleanly after 30 seconds. Continuing...");
        }
    }

#region Internal settings
    private DiscordSocketConfig GetSocketConfig(int shardId) => new() {
        ShardId = shardId,
        TotalShards = Config.Sharding.Total,
        LogLevel = LogSeverity.Info,
        DefaultRetryMode = RetryMode.Retry502 | RetryMode.RetryTimeouts,
        GatewayIntents = GatewayIntents.Guilds,
        SuppressUnknownDispatchWarnings = true,
        LogGatewayIntentWarnings = false,
        FormatUsersInBidirectionalUnicode = false
    };

    internal static void BuildSqlOptions(DbContextOptionsBuilder options) =>
        options.UseNpgsql(Program.SqlConnectionString)
               .UseSnakeCaseNamingConvention();
#endregion

    private void Log(string message) => Program.Log(nameof(ShardManager), message);

    /// <summary>
    /// Creates and sets up a new shard instance.
    /// </summary>
    private async Task<ShardInstance> InitializeShard(int shardId) {
        // Each shard gets its own unique config and service collection.
        // The shard belongs in its own collection, gets initialized, then retrieved for the manager.
        var localConf = GetSocketConfig(shardId);
        var services = new ServiceCollection()
            .AddSingleton(s => new ShardInstance(this, s))
            .AddSingleton(new UserCache())
            .AddSingleton(new DiscordSocketClient(localConf))
            .AddDbContext<BotDatabaseContext>(BuildSqlOptions)
            .BuildServiceProvider();
        var newInstance = services.GetRequiredService<ShardInstance>();
        await newInstance.StartAsync().ConfigureAwait(false);

        return newInstance;
    }

    public int? GetShardIdFor(ulong guildId) {
        foreach (var sh in _shards.Values) {
            if (sh == null) continue;
            if (sh.DiscordClient.GetGuild(guildId) != null) return sh.ShardId;
        }
        return null;
    }

    private async Task StatusLoop() {
        try {
            do {
                var startAllowance = Config.Sharding.Interval;

                // Iterate through shards, create report on each
                var shardStatuses = new StringBuilder();
                foreach (var i in _shards.Keys) {
                    shardStatuses.Append($"Shard {i:00}: ");

                    if (_shards[i] == null) {
                        if (startAllowance > 0) {
                            shardStatuses.AppendLine("Started.");
                            _shards[i] = await InitializeShard(i).ConfigureAwait(false);
                            startAllowance--;
                        } else {
                            shardStatuses.AppendLine("Awaiting start.");
                        }
                        continue;
                    }

                    var shard = _shards[i]!;
                    var client = shard.DiscordClient;
                    shardStatuses.Append($"{Enum.GetName(typeof(ConnectionState), client.ConnectionState)} ({client.Latency:000}ms).");
                    shardStatuses.Append($" Guilds: {client.Guilds.Count:0000},");
                    shardStatuses.Append($" Cache: {shard.Cache.GuildsCount:000} guilds -> {shard.Cache.UsersCount:00000} users");
                    shardStatuses.Append($" Task: {shard.CurrentExecutingService ?? "Idle"}");
                    var lastRun = DateTimeOffset.UtcNow - shard.LastBackgroundRun;
                    shardStatuses.Append($" since {Math.Floor(lastRun.TotalMinutes):00}m{lastRun.Seconds:00}s ago.");
                    shardStatuses.AppendLine();
                }
                Log(shardStatuses.ToString().TrimEnd());
                var ct = GetTotalGuildCount();
                Log($"Total guilds: {ct:00,000} - Average shard load: {(double)ct / _shards.Count:0000.0}");
                Log($"Uptime: {Program.BotUptime}");

                await Task.Delay(Config.StatusInterval * 1000, _mainCancel.Token).ConfigureAwait(false);
            } while (!_mainCancel.IsCancellationRequested);
        } catch (TaskCanceledException) { }
    }

    public int GetTotalGuildCount() => (from sh in _shards
                                        where sh.Value != null
                                        select sh.Value.DiscordClient.Guilds.Count)
                                        .Sum();
}