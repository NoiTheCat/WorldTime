global using Discord;
global using Discord.WebSocket;
using System.Text;

namespace WorldTime;

/// <summary>
/// Main class for the program. Configures the client on start and occasionally prints status information.
/// </summary>
internal class WorldTime : IDisposable {
    /// <summary>
    /// Number of seconds between each time the status task runs, in seconds.
    /// </summary>
#if DEBUG
    private const int StatusInterval = 20;
#else
    private const int StatusInterval = 300;
#endif

    /// <summary>
    /// Number of concurrent shard startups to happen on each check.
    /// This value is also used in <see cref="DataRetention"/>.
    /// </summary>
    public const int MaxConcurrentOperations = 5;

    private readonly Task _statusTask;
    private readonly CancellationTokenSource _mainCancel;
    private readonly Commands _commands;

    internal Configuration Config { get; }
    internal DiscordShardedClient DiscordClient { get; }
    internal Database Database { get; }

    public WorldTime(Configuration cfg, Database d) {
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Program.Log(nameof(WorldTime), $"Version {ver!.ToString(3)} is starting...");

        Config = cfg;
        Database = d;

        // Configure client
        DiscordClient = new DiscordShardedClient(new DiscordSocketConfig() {
            TotalShards = Config.ShardTotal,
            LogLevel = LogSeverity.Info,
            DefaultRetryMode = RetryMode.RetryRatelimit,
            MessageCacheSize = 0, // disable message cache
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.GuildMessages
        });
        DiscordClient.Log += DiscordClient_Log;
        DiscordClient.ShardReady += DiscordClient_ShardReady;
        DiscordClient.MessageReceived += DiscordClient_MessageReceived;
        _commands = new Commands(this, Database);

        // Start status reporting thread
        _mainCancel = new CancellationTokenSource();
        _statusTask = Task.Factory.StartNew(StatusLoop, _mainCancel.Token,
                                              TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public async Task StartAsync() {
        await DiscordClient.LoginAsync(TokenType.Bot, Config.BotToken).ConfigureAwait(false);
        await DiscordClient.StartAsync().ConfigureAwait(false);
    }

    public void Dispose() {
        _mainCancel.Cancel();
        _statusTask.Wait(10000);
        if (!_statusTask.IsCompleted)
            Program.Log(nameof(WorldTime), "Warning: Main thread did not cleanly finish up in time. Continuing...");

        _mainCancel.Cancel();
        _statusTask.Wait(5000);
        _mainCancel.Dispose();

        Program.Log(nameof(WorldTime), $"Uptime: {Program.BotUptime}");
    }

    private async Task StatusLoop() {
        try {
            await Task.Delay(30000, _mainCancel.Token).ConfigureAwait(false); // initial 30 second delay
            while (!_mainCancel.IsCancellationRequested) {
                Program.Log(nameof(WorldTime), $"Bot uptime: {Program.BotUptime}");
                
                await PeriodicReport(DiscordClient.CurrentUser.Id, DiscordClient.Guilds.Count, _mainCancel.Token).ConfigureAwait(false);
                await Task.Delay(StatusInterval * 1000, _mainCancel.Token).ConfigureAwait(false);
            }
        } catch (TaskCanceledException) { }
    }

    private static readonly HttpClient _httpClient = new();
    /// <summary>
    /// Called by the status loop. Reports guild count to the console and to external services.
    /// </summary>
    private async Task PeriodicReport(ulong botId, int guildCount, CancellationToken cancellationToken) {
        var avg = (float)guildCount / Config.ShardTotal;
        Program.Log("Report", $"Currently in {guildCount} guilds. Average shard load: {avg:0.0}.");
        if (botId == 0) return;

        // Discord Bots
        if (!string.IsNullOrEmpty(Config.DBotsToken)) {
            try {
                string dBotsApiUrl = $"https://discord.bots.gg/api/v1/bots/{ botId }/stats";
                var body = $"{{ \"guildCount\": {guildCount} }}";
                var uri = new Uri(string.Format(dBotsApiUrl));

                var post = new HttpRequestMessage(HttpMethod.Post, uri);
                post.Headers.Add("Authorization", Config.DBotsToken);
                post.Content = new StringContent(body,
                    Encoding.UTF8, "application/json");

                await _httpClient.SendAsync(post, cancellationToken);
                Program.Log("Discord Bots", "Update successful.");
            } catch (Exception ex) {
                Program.Log("Discord Bots", "Exception encountered during update: " + ex.Message);
            }
        }
    }

#region Event handling
    private Task DiscordClient_Log(LogMessage arg) {
        // Suppress certain messages
        if (arg.Message != null) {
            switch (arg.Message) {
                case "Connecting":
                case "Connected":
                case "Ready":
            //    case "Failed to resume previous session":
            //    case "Resumed previous session":
                case "Disconnecting":
                case "Disconnected":
            //    case "WebSocket connection was closed":
                    return Task.CompletedTask;
            }
            if (arg.Message == "Heartbeat Errored") {
                // Replace this with a custom message; do not show stack trace
                Program.Log("Discord.Net", $"{arg.Severity}: {arg.Message} - {arg.Exception.Message}");
                return Task.CompletedTask;
            }

            Program.Log("Discord.Net", $"{arg.Severity}: {arg.Message}");
        }

        // Suppress certain exceptions
        if (arg.Exception != null) {
            if (arg.Exception is not GatewayReconnectException)
                Program.Log("Discord.Net exception", arg.Exception.ToString());
        }

        return Task.CompletedTask;
    }

    private Task DiscordClient_ShardReady(DiscordSocketClient arg) => arg.SetGameAsync(Commands.CommandPrefix + "help");

    /// <summary>
    /// Non-specific handler for incoming events.
    /// </summary>
    private async Task DiscordClient_MessageReceived(SocketMessage message) {
        if (message.Author.IsWebhook) return;
        if (message.Type != MessageType.Default) return;
        if (message.Channel is not SocketTextChannel channel) return;

        /*
         * From https://support-dev.discord.com/hc/en-us/articles/4404772028055:
         * "You will still receive the events and can call the same APIs, and you'll get other data about a message like
         * author and timestamp. To put it simply, you'll be able to know all the information about when someone sends a
         * message; you just won't know what they said."
         * 
         * Assuming this stays true, it will be possible to maintain legacy behavior after this bot loses full access to incoming messages.
         */
        // Attempt to update user's last_seen column
        // POTENTIAL BUG: If user does a list command, the list may be processed before their own time's refreshed, and they may be skipped.
        var hasMemberHint = await Database.UpdateLastActivityAsync((SocketGuildUser)message.Author).ConfigureAwait(false);

        // Proactively fill guild user cache if the bot has any data for the respective guild
        // Can skip an extra query if the last_seen update is known to have been successful, otherwise query for any users
        var guild = channel.Guild;
        if (!guild.HasAllMembers && (hasMemberHint || await Database.HasAnyAsync(guild).ConfigureAwait(false))) {
            // Event handler hangs if awaited normally or used with Task.Run
            await Task.Factory.StartNew(guild.DownloadUsersAsync).ConfigureAwait(false);
        }
    }
    #endregion
}
