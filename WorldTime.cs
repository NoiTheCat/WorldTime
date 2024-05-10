global using Discord;
global using Discord.WebSocket;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Text;
using WorldTime.Data;

namespace WorldTime;
/// <summary>
/// Main class for the program. Configures the client on start and occasionally prints status information.
/// </summary>
internal class WorldTime : IDisposable {
    /// <summary>
    /// Number of seconds between each time the status task runs, in seconds.
    /// </summary>
#if DEBUG
    internal const int StatusInterval = 20;
#else
    internal const int StatusInterval = 300;
#endif

    private readonly Task _statusTask;
    private readonly CancellationTokenSource _statusCancel;
    private readonly IServiceProvider _services;
    private readonly BackgroundUserListLoad _bgFetch;

    internal Configuration Config { get; }
    internal DiscordShardedClient DiscordClient => _services.GetRequiredService<DiscordShardedClient>();

    public WorldTime(Configuration cfg) {
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        Program.Log(nameof(WorldTime), $"Version {ver!.ToString(3)} is starting...");

        Config = cfg;

        // Configure client, set up command handling
        var clientConf = new DiscordSocketConfig() {
            LogLevel = LogSeverity.Info,
            DefaultRetryMode = RetryMode.RetryRatelimit,
            MessageCacheSize = 0, // disable message cache
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMembers,
            SuppressUnknownDispatchWarnings = true,
            LogGatewayIntentWarnings = false,
            FormatUsersInBidirectionalUnicode = false
        };
        _services = new ServiceCollection()
            .AddSingleton(new DiscordShardedClient(clientConf))
            .AddSingleton(s => new InteractionService(s.GetRequiredService<DiscordShardedClient>()))
            .AddTransient(typeof(BotDatabaseContext))
            .BuildServiceProvider();
        DiscordClient.Log += DiscordClient_Log;
        DiscordClient.ShardReady += DiscordClient_ShardReady;
        var iasrv = _services.GetRequiredService<InteractionService>();
        DiscordClient.InteractionCreated += DiscordClient_InteractionCreated;
        iasrv.SlashCommandExecuted += InteractionService_SlashCommandExecuted;

        // Start status reporting thread
        _statusCancel = new CancellationTokenSource();
        _statusTask = Task.Factory.StartNew(StatusLoop, _statusCancel.Token,
                                              TaskCreationOptions.LongRunning, TaskScheduler.Default);

        _bgFetch = new(_services);
    }

    public async Task StartAsync() {
        await _services.GetRequiredService<InteractionService>().AddModulesAsync(Assembly.GetExecutingAssembly(), _services);
        await DiscordClient.LoginAsync(TokenType.Bot, Config.BotToken).ConfigureAwait(false);
        await DiscordClient.StartAsync().ConfigureAwait(false);
    }

    public void Dispose() {
        _statusCancel.Cancel();
        _statusTask.Wait(10000);
        if (!_statusTask.IsCompleted)
            Program.Log(nameof(WorldTime), "Warning: Main thread did not cleanly finish up in time. Continuing...");

        _statusCancel.Dispose();

        Program.Log(nameof(WorldTime), $"Uptime: {Program.BotUptime}");
    }

    private async Task StatusLoop() {
        try {
            await Task.Delay(30000, _statusCancel.Token).ConfigureAwait(false); // initial 30 second delay
            while (!_statusCancel.IsCancellationRequested) {
                Program.Log(nameof(WorldTime), $"Bot uptime: {Program.BotUptime}");
                
                await PeriodicReport(DiscordClient.CurrentUser.Id, DiscordClient.Guilds.Count, _statusCancel.Token).ConfigureAwait(false);
                await Task.Delay(StatusInterval * 1000, _statusCancel.Token).ConfigureAwait(false);
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
                var dBotsApiUrl = $"https://discord.bots.gg/api/v1/bots/{ botId }/stats";
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
            switch (arg.Message) { // Connection status messages replaced by ShardManager's output
                case "Connecting":
                case "Connected":
                case "Ready":
                case "Disconnecting":
                case "Disconnected":
                case "Resumed previous session":
                case "Failed to resume previous session":
                case "Discord.WebSocket.GatewayReconnectException: Server requested a reconnect":
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

    private async Task DiscordClient_ShardReady(DiscordSocketClient arg) {
#if !DEBUG
        // Update slash/interaction commands
        if (arg.ShardId == 0) {
            await _services.GetRequiredService<InteractionService>().RegisterCommandsGloballyAsync();
            Program.Log("Command registration", "Updated global command registration.");
        }
#else
        // Debug: Register our commands locally instead, in each guild we're in
        if (arg.Guilds.Count > 5) {
            Program.Log("Command registration", "Are you debugging in production?! Skipping DEBUG command registration.");
        } else {
            var iasrv = _services.GetRequiredService<InteractionService>();
            foreach (var g in arg.Guilds) {
                await iasrv.RegisterCommandsToGuildAsync(g.Id, true).ConfigureAwait(false);
                Program.Log("Command registration", $"Updated DEBUG command registration in guild {g.Id}.");
            }
        }
#endif
    }

    const string InternalError = ":x: An unknown error occurred. If it persists, please notify the bot owner.";

    // Slash command preparation and invocation
    private async Task DiscordClient_InteractionCreated(SocketInteraction arg) {
        var context = new ShardedInteractionContext(DiscordClient, arg);

        try {
            await _services.GetRequiredService<InteractionService>().ExecuteCommandAsync(context, _services);
        } catch (Exception ex) {
            Program.Log(nameof(DiscordClient_InteractionCreated), $"Unhandled exception. {ex}");
            if (arg.Type == InteractionType.ApplicationCommand) {
                if (arg.HasResponded) await arg.ModifyOriginalResponseAsync(prop => prop.Content = InternalError);
                else await arg.RespondAsync(InternalError);
            }
        }
    }

    // Slash command logging and failed execution handling
    private static async Task InteractionService_SlashCommandExecuted(SlashCommandInfo info, IInteractionContext context, IResult result) {
        string sender;
        if (context.Guild != null) {
            sender = $"{context.Guild}!{context.User}";
        } else {
            sender = $"{context.User} in non-guild context";
        }
        var logresult = $"{(result.IsSuccess ? "Success" : "Fail")}: `/{info}` by {sender}.";

        if (result.Error != null) {
            // Additional log information with error detail
            logresult += " " + Enum.GetName(typeof(InteractionCommandError), result.Error) + ": " + result.ErrorReason;

            // Generic error response
            // TODO when implementing proper application error logging, see here
            var ia = context.Interaction;
            if (ia.HasResponded) await ia.ModifyOriginalResponseAsync(p => p.Content = InternalError);
            else await ia.RespondAsync(InternalError);
        }

        Program.Log("Command", logresult);
    }
    #endregion
}
