using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using WorldTime.BackgroundServices;
using WorldTime.Caching;
using WorldTime.Config;

namespace WorldTime;
/// <summary>
/// Single shard instance for World Time. This shard independently handles all input and output to Discord.
/// </summary>
public sealed class ShardInstance : IDisposable {
    private readonly ShardManager _manager;
    private readonly ShardBackgroundWorker _background;
    private readonly IServiceProvider _services;

    internal DiscordSocketClient DiscordClient { get; }
    public int ShardId => DiscordClient.ShardId;
    internal Configuration Config => _manager.Config;
    internal UserCache Cache { get; }
    internal Coordinator Fetcher { get; }

    internal DateTimeOffset LastBackgroundRun => _background.LastBackgroundRun;
    internal string? CurrentExecutingService => _background.CurrentExecutingService;

    public const string InternalError = ":x: An unknown error occurred. If it persists, please notify the bot owner.";

    /// <summary>
    /// Sets up a dummy shard instance to use for early initialization of InteractionService.
    /// </summary>
    public ShardInstance(IServiceProvider localServices) {
        _manager = null!;
        _background = null!;
        Fetcher = null!;

        _services = localServices;
        Cache = _services.GetRequiredService<UserCache>();
        DiscordClient = _services.GetRequiredService<DiscordSocketClient>();
    }

    /// <summary>
    /// Prepares and configures the shard instances, but does not yet start its connection.
    /// </summary>
    internal ShardInstance(ShardManager manager, IServiceProvider localServices) {
        _manager = manager;
        Fetcher = new Coordinator(this);

        _services = localServices;
        Cache = _services.GetRequiredService<UserCache>();
        DiscordClient = _services.GetRequiredService<DiscordSocketClient>();

        DiscordClient.Log += Client_Log;
        DiscordClient.InteractionCreated += DiscordClient_InteractionCreated;

        // Background task constructor begins background processing immediately.
        _background = new ShardBackgroundWorker(this);
    }

    /// <summary>
    /// Starts up this shard's connection to Discord and background task handling associated with it.
    /// </summary>
    public async Task StartAsync() {
        await DiscordClient.LoginAsync(TokenType.Bot, Config.BotToken).ConfigureAwait(false);
        await DiscordClient.StartAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Does all necessary steps to stop this shard, including canceling background tasks and disconnecting.
    /// </summary>
    public void Dispose() {
        DiscordClient.InteractionCreated -= DiscordClient_InteractionCreated;
        _background.Dispose();
        if (!DiscordClient.LogoutAsync().Wait(5000)) {
            Log("Shutdown", "Hanging on logout! Continuing with dispose.");
        }
        DiscordClient.Dispose();
    }

    internal void Log(string source, string message) => Program.Log($"Shard {ShardId:00}] [{source}", message);

    private Task Client_Log(LogMessage arg) {
        // Suppress certain messages
        if (arg.Message != null) {
            if (!Config.LogConnectionStatus) {
                switch (arg.Message) {
                    case "Connecting":
                    case "Connected":
                    case "Ready":
                    case "Disconnecting":
                    case "Disconnected":
                    case "Resumed previous session":
                    case "Failed to resume previous session":
                    case "Serializer Error": // The exception associated with this log appears a lot as of v3.2-ish
                    case var s when s.StartsWith("Rate limit triggered"):
                        return Task.CompletedTask;
                }
            }
            Log("Discord.Net", $"{arg.Severity}: {arg.Message}");
        }

        if (arg.Exception != null) {
            if (!Config.LogConnectionStatus) {
                if (arg.Exception is GatewayReconnectException || arg.Exception.Message == "WebSocket connection was closed")
                    return Task.CompletedTask;
            }

            if (arg.Exception is TaskCanceledException) return Task.CompletedTask; // We don't ever need to know these...
            Log("Discord.Net exception", $"{arg.Exception.GetType().FullName}: {arg.Exception.Message}");
        }

        return Task.CompletedTask;
    }

    // Slash command preparation and invocation
    private async Task DiscordClient_InteractionCreated(SocketInteraction arg) {
        var context = new SocketInteractionContext(DiscordClient, arg);
        try {
            await _manager.Interactions.ExecuteCommandAsync(context, _services).ConfigureAwait(false);
        } catch (Exception e) {
            Log(nameof(DiscordClient_InteractionCreated), $"Unhandled exception. {e}");
            if (arg.Type == InteractionType.ApplicationCommand) {
                if (arg.HasResponded) await arg.ModifyOriginalResponseAsync(prop => prop.Content = InternalError);
                else await arg.RespondAsync(InternalError);
            }
        }
    }

    // Gets total guild count from manager - for help command
    public int GetTotalGuildCount() => _manager.GetTotalGuildCount();
}
