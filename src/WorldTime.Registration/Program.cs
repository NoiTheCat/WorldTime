using Discord.WebSocket;

var client = new DiscordSocketClient();
    var sh = new WorldTime.ShardInstance();
#if DEBUG
    // Debug: Register our commands locally instead, in each guild we're in
    if (DiscordClient.Guilds.Count > 5) {
        Program.Log(nameof(ShardInstance), "Are you debugging in production?! Skipping DEBUG command registration.");
        return;
    } else {
        var ia = new InteractionService(DiscordClient);
        await ia.AddModulesAsync(Assembly.GetExecutingAssembly(), _services).ConfigureAwait(false);
        foreach (var g in DiscordClient.Guilds) {
            await ia.RegisterCommandsToGuildAsync(g.Id, true).ConfigureAwait(false);
            Log(nameof(ShardInstance), $"Updated DEBUG command registration in guild {g.Id}.");
        }
    }
#else
        // Update slash/interaction commands
        if (ShardId == 0) {
            var ia = new Discord.Interactions.InteractionService(DiscordClient);
            await ia.AddModulesAsync(Assembly.GetExecutingAssembly(), _services).ConfigureAwait(false);
            await ia.RegisterCommandsGloballyAsync(true);
            Log(nameof(ShardInstance), "Updated global command registration.");
        }
#endif