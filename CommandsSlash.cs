using System.Text;

namespace WorldTime;

internal class CommandsSlash : CommandsCommon {
    delegate Task CommandResponder(SocketSlashCommand arg);

    const string ErrGuildOnly = ":x: This command can only be run within a server.";
    const string ErrNotAllowed = ":x: Only server moderators may use this command.";

    const string EmbedHelpField1 = $"`/help` - {HelpHelp}\n"
        + $"`/list` - {HelpList}\n"
        + $"`/set` - {HelpSet}\n"
        + $"`/remove` - {HelpRemove}";
    const string EmbedHelpField2 = $"`/set-for` - {HelpSetFor}\n`/remove-for` - {HelpRemoveFor}";

    #region Help strings
    const string HelpHelp = "Displays a list of available bot commands.";
    const string HelpList = "Shows the current time for all recently active known users.";
    const string HelpSet = "Adds or updates your time zone to the bot.";
    const string HelpSetFor = "Sets/updates time zone for a given user.";
    const string HelpRemove = "Removes your time zone information from this bot.";
    const string HelpRemoveFor = "Removes time zone for a given user.";
    #endregion

    public CommandsSlash(WorldTime inst, Database db) : base(db, inst) {
        inst.DiscordClient.SlashCommandExecuted += DiscordClient_SlashCommandExecuted;
        inst.DiscordClient.ShardReady += DiscordClient_ShardReady;
    }

    private async Task DiscordClient_ShardReady(DiscordSocketClient arg) {
#if !DEBUG
        // Update our commands here, only when the first shard connects
        if (arg.ShardId != 0) return;
#endif
        var cmds = new ApplicationCommandProperties[] {
            new SlashCommandBuilder()
                .WithName("help").WithDescription(HelpHelp).Build(),
            new SlashCommandBuilder()
                .WithName("list")
                .WithDescription(HelpList)
                .AddOption("user", ApplicationCommandOptionType.User, "A specific user whose time to look up.", isRequired: false)
                .Build(),
            new SlashCommandBuilder()
                .WithName("set")
                .WithDescription(HelpSet)
                .AddOption("zone", ApplicationCommandOptionType.String, "The new time zone to set.", isRequired: true)
                .Build(),
            new SlashCommandBuilder()
                .WithName("set-for")
                .WithDescription(HelpSetFor)
                .AddOption("user", ApplicationCommandOptionType.User, "The user whose time zone to modify.", isRequired: true)
                .AddOption("zone", ApplicationCommandOptionType.String, "The new time zone to set.", isRequired: true)
                .Build(),
            new SlashCommandBuilder()
                .WithName("remove").WithDescription(HelpRemove).Build(),
            new SlashCommandBuilder()
                .WithName("remove-for")
                .WithDescription(HelpRemoveFor)
                .AddOption("user", ApplicationCommandOptionType.User, "The user whose time zone to remove.", isRequired: true)
                .Build()
        };
#if !DEBUG
        // Remove any unneeded/unused commands
        var existingcmdnames = cmds.Select(c => c.Name.Value).ToHashSet();
        foreach (var gcmd in await arg.GetGlobalApplicationCommandsAsync()) {
            if (!existingcmdnames.Contains(gcmd.Name)) {
                Program.Log("Command registration", $"Found registered unused command /{gcmd.Name} - sending removal request");
                await gcmd.DeleteAsync();
            }
        }
        // And update what we have
        Program.Log("Command registration", $"Bulk updating {cmds.Length} global command(s)");
        await arg.BulkOverwriteGlobalApplicationCommandsAsync(cmds).ConfigureAwait(false);
#else
        // Debug: Register our commands locally instead, in each guild we're in
        foreach (var g in arg.Guilds) {
            await g.DeleteApplicationCommandsAsync().ConfigureAwait(false);
            await g.BulkOverwriteApplicationCommandAsync(cmds).ConfigureAwait(false);
        }

        foreach (var gcmd in await arg.GetGlobalApplicationCommandsAsync()) {
            Program.Log("Command registration", $"Found global command /{gcmd.Name} and we're DEBUG - sending removal request");
            await gcmd.DeleteAsync();
        }
#endif
    }

    private async Task DiscordClient_SlashCommandExecuted(SocketSlashCommand arg) {
        SocketGuildChannel? rptChannel = arg.Channel as SocketGuildChannel;
        var rptId = rptChannel?.Guild.Id ?? arg.User.Id;
        Program.Log("Command executed", $"/{arg.CommandName} by {arg.User} in { (rptChannel != null ? "guild" : "DM. User") } ID {rptId}");

        CommandResponder responder = arg.Data.Name switch {
            "help" => CmdHelp,
            "list" => CmdList,
            "set" => CmdSet,
            "set-for" => CmdSetFor,
            "remove" => CmdRemove,
            "remove-for" => CmdRemoveFor,
            _ => UnknownCommandHandler
        };
        try {
            await responder(arg).ConfigureAwait(false);
        } catch (Exception e) {
            Program.Log("Command exception", e.ToString());
            // TODO respond with error message?
        }
    }

    private async Task UnknownCommandHandler(SocketSlashCommand arg) {
        Program.Log("Command invoked", $"/{arg.Data.Name} is an unknown command!");
        await arg.RespondAsync("Oops, that command isn't supposed to be there... Please try something else.",
            ephemeral: true).ConfigureAwait(false);
    }

    private async Task CmdHelp(SocketSlashCommand arg) {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
        var guildct = _instance.DiscordClient.Guilds.Count;
        var uniquetz = await _database.GetDistinctZoneCountAsync();
        await arg.RespondAsync(embed: new EmbedBuilder() {
            Title = "Help & About",
            Description = $"World Time v{version} - Serving {guildct} communities across {uniquetz} time zones.\n\n"
                + "This bot is provided for free, without any paywalled 'premium' features. "
                + "If you've found this bot useful, please consider contributing via the "
                + "bot author's page on Ko-fi: https://ko-fi.com/noithecat.",
            Footer = new EmbedFooterBuilder() {
                IconUrl = _instance.DiscordClient.CurrentUser.GetAvatarUrl(),
                Text = "World Time"
            }
        }.AddField(inline: false, name: "Commands", value: EmbedHelpField1
        ).AddField(inline: false, name: "Admin commands", value: EmbedHelpField2
        ).AddField(inline: false, name: "Zones", value:
            "This bot accepts zone names from the IANA Time Zone Database (a.k.a. Olson Database). " +
            "A useful tool to determine yours can be found at: https://kevinnovak.github.io/Time-Zone-Picker/"
        ).Build());
    }

    private async Task CmdList(SocketSlashCommand arg) {
        if (arg.Channel is not SocketGuildChannel gc) {
            await arg.RespondAsync(ErrGuildOnly).ConfigureAwait(false);
            return;
        }

        if (arg.Data.Options.FirstOrDefault()?.Value is SocketGuildUser parameter) {
            await CmdListUser(arg, parameter);
            return;
        }

        var guild = gc.Guild;
        if (!await AreUsersDownloadedAsync(guild).ConfigureAwait(false)) {
            await arg.RespondAsync(ErrNoUserCache, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var userlist = await _database.GetGuildZonesAsync(guild.Id).ConfigureAwait(false);
        if (userlist.Count == 0) {
            await arg.RespondAsync(":x: Nothing to show. " +
                $"To register your time zone with the bot, use the `/set` command.").ConfigureAwait(false);
            return;
        }
        // Order times by popularity to limit how many are shown, group by printed name
        var sortedlist = new SortedDictionary<string, List<ulong>>();
        foreach ((string area, List<ulong> users) in userlist.OrderByDescending(o => o.Value.Count).Take(20)) {
            // Filter further to top 20 distinct timezones, even if they are not displayed in the final result
            var areaprint = TzPrint(area);
            if (!sortedlist.ContainsKey(areaprint)) sortedlist.Add(areaprint, new List<ulong>());
            sortedlist[areaprint].AddRange(users);
        }

        // Build zone listings with users
        var outputlines = new List<string>();
        foreach ((string area, List<ulong> users) in sortedlist) {
            var buffer = new StringBuilder();
            buffer.Append(area[4..] + ": ");
            bool empty = true;
            foreach (var userid in users) {
                var userinstance = guild.GetUser(userid);
                if (userinstance == null) continue;
                if (empty) empty = !empty;
                else buffer.Append(", ");
                var useradd = FormatName(userinstance);
                if (buffer.Length + useradd.Length > MaxSingleLineLength) {
                    buffer.Append("others...");
                    break;
                } else buffer.Append(useradd);
            }
            if (!empty) outputlines.Add(buffer.ToString());
        }

        // Prepare for output - send buffers out if they become too large
        outputlines.Sort();
        bool hasOutputOneLine = false;
        // First output is shown as an interaction response, followed then as regular channel messages
        async Task doOutput(Embed msg) {
            if (!hasOutputOneLine) {
                await arg.RespondAsync(embed: msg).ConfigureAwait(false);
                hasOutputOneLine = true;
            } else {
                await arg.Channel.SendMessageAsync(embed: msg).ConfigureAwait(false);
            }
        }

        var resultout = new StringBuilder();
        foreach (var line in outputlines) {
            if (resultout.Length + line.Length > MaxSingleOutputLength) {
                await doOutput(new EmbedBuilder().WithDescription(resultout.ToString()).Build()).ConfigureAwait(false);
                resultout.Clear();
            }
            if (resultout.Length > 0) resultout.AppendLine(); // avoids trailing newline by adding to the previous line
            resultout.Append(line);
        }
        if (resultout.Length > 0) {
            await doOutput(new EmbedBuilder().WithDescription(resultout.ToString()).Build()).ConfigureAwait(false);
        }
    }

    private async Task CmdListUser(SocketSlashCommand arg, SocketGuildUser parameter) {
        // Not meant as a command handler - called by CmdList
        var result = await _database.GetUserZoneAsync(parameter).ConfigureAwait(false);
        if (result == null) {
            bool isself = arg.User.Id == parameter.Id;
            if (isself) await arg.RespondAsync(":x: You do not have a time zone. Set it with `tz.set`.", ephemeral: true)
                    .ConfigureAwait(false);
            else await arg.RespondAsync(":x: The given user does not have a time zone set.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var resulttext = TzPrint(result)[4..] + ": " + FormatName(parameter);
        await arg.RespondAsync(embed: new EmbedBuilder().WithDescription(resulttext).Build()).ConfigureAwait(false);
    }

    private async Task CmdSet(SocketSlashCommand arg) {
        if (arg.Channel is not SocketGuildChannel) {
            await arg.RespondAsync(ErrGuildOnly).ConfigureAwait(false);
            return;
        }

        var input = (string)arg.Data.Options.First().Value;
        input = ParseTimeZone(input);
        if (input == null) {
            await arg.RespondAsync(ErrInvalidZone, ephemeral: true).ConfigureAwait(false);
            return;
        }
        await _database.UpdateUserAsync((SocketGuildUser)arg.User, input).ConfigureAwait(false);
        await arg.RespondAsync($":white_check_mark: Your time zone has been set to **{input}**.").ConfigureAwait(false);
    }

    private async Task CmdSetFor(SocketSlashCommand arg) {
        if (arg.Channel is not SocketGuildChannel) {
            await arg.RespondAsync(ErrGuildOnly).ConfigureAwait(false);
            return;
        }

        if (!IsUserAdmin((SocketGuildUser)arg.User)) {
            await arg.RespondAsync(ErrNotAllowed, ephemeral: true).ConfigureAwait(false);
            return;
        }

        // Extract parameters
        var opts = arg.Data.Options.ToDictionary(o => o.Name, o => o);
        var user = (SocketGuildUser)opts["user"].Value;
        var zone = (string)opts["zone"].Value;

        var newtz = ParseTimeZone(zone);
        if (newtz == null) {
            await arg.RespondAsync(ErrInvalidZone).ConfigureAwait(false);
            return;
        }

        await _database.UpdateUserAsync(user, newtz).ConfigureAwait(false);
        await arg.RespondAsync($":white_check_mark: Time zone for **{user}** set to **{newtz}**.").ConfigureAwait(false);
    }

    private async Task CmdRemove(SocketSlashCommand arg) {
        if (arg.Channel is not SocketGuildChannel) {
            await arg.RespondAsync(ErrGuildOnly).ConfigureAwait(false);
            return;
        }

        var success = await _database.DeleteUserAsync((SocketGuildUser)arg.User).ConfigureAwait(false);
        if (success) await arg.RespondAsync(":white_check_mark: Your zone has been removed.").ConfigureAwait(false);
        else await arg.RespondAsync(":x: You don't have a time zone set.").ConfigureAwait(false);
    }

    private async Task CmdRemoveFor(SocketSlashCommand arg) {
        if (arg.Channel is not SocketGuildChannel) {
            await arg.RespondAsync(ErrGuildOnly).ConfigureAwait(false);
            return;
        }

        if (!IsUserAdmin((SocketGuildUser)arg.User)) {
            await arg.RespondAsync(ErrNotAllowed, ephemeral: true).ConfigureAwait(false);
            return;
        }
    }
}
