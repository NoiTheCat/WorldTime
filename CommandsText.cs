using System.Text;
using System.Text.RegularExpressions;

namespace WorldTime;

internal class CommandsText : CommandsCommon {
#if DEBUG
    public const string CommandPrefix = "tt.";
#else
    public const string CommandPrefix = "tz.";
#endif
    delegate Task Command(SocketTextChannel channel, SocketGuildUser sender, SocketMessage message);

    private readonly Dictionary<string, Command> _commands;
    
    private static readonly Regex _userExplicit;
    private static readonly Regex _userMention;

    static CommandsText() {
        _userExplicit = new Regex(@"(.+)#(\d{4})", RegexOptions.Compiled);
        _userMention = new Regex(@"\!?(\d+)>", RegexOptions.Compiled);
    }

    public CommandsText(WorldTime inst, Database db) : base(db, inst) {
        _commands = new(StringComparer.OrdinalIgnoreCase) {
            { "help", CmdHelp },
            { "list", CmdList },
            { "set", CmdSet },
            { "remove", CmdRemove },
            { "setfor", CmdSetFor },
            { "removefor", CmdRemoveFor }
        };

        inst.DiscordClient.MessageReceived += CommandDispatch;
    }

    private async Task CommandDispatch(SocketMessage message) {
        if (message.Author.IsBot || message.Author.IsWebhook) return;
        if (message.Type != MessageType.Default) return;
        if (message.Channel is not SocketTextChannel channel) return; // not handling DMs

        var msgsplit = message.Content.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (msgsplit.Length == 0 || msgsplit[0].Length < 4) return;
        if (msgsplit[0].StartsWith(CommandPrefix, StringComparison.OrdinalIgnoreCase)) { // TODO add support for multiple prefixes?
            var cmdBase = msgsplit[0][3..];
            if (_commands.ContainsKey(cmdBase)) {
                Program.Log("Command invoked", $"{channel.Guild.Name}/{message.Author} {message.Content}");
                try {
                    await _commands[cmdBase](channel, (SocketGuildUser)message.Author, message).ConfigureAwait(false);
                } catch (Exception ex) {
                    Program.Log("Command invoked", ex.ToString());
                }
            }
        }
    }

    private async Task CmdHelp(SocketTextChannel channel, SocketGuildUser sender, SocketMessage message) {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
        var guildct = _instance.DiscordClient.Guilds.Count;
        var uniquetz = await _database.GetDistinctZoneCountAsync();
        await channel.SendMessageAsync(embed: new EmbedBuilder() {
            Color = new Color(0xe0f2f7),
            Title = "Help & About",
            Description = $"World Time v{version} - Serving {guildct} communities across {uniquetz} time zones.\n\n"
                + "This bot is provided for free, without any paywalled 'premium' features. "
                + "If you've found this bot useful, please consider contributing via the "
                + "bot author's page on Ko-fi: https://ko-fi.com/noithecat.",
            Footer = new EmbedFooterBuilder() {
                IconUrl = _instance.DiscordClient.CurrentUser.GetAvatarUrl(),
                Text = "World Time"
            }
        }.AddField(inline: false, name: "Commands", value:
            $"`{CommandPrefix}help` - This message.\n" +
            $"`{CommandPrefix}list` - Displays current times for all recently active known users.\n" +
            $"`{CommandPrefix}list [user]` - Displays the current time for the given *user*.\n" +
            $"`{CommandPrefix}set [zone]` - Registers or updates your *zone* with the bot.\n" +
            $"`{CommandPrefix}remove` - Removes your name from this bot."
        ).AddField(inline: false, name: "Admin commands", value:
            $"`{CommandPrefix}setFor [user] [zone]` - Sets the time zone for another user.\n" +
            $"`{CommandPrefix}removeFor [user]` - Removes another user's information."
        ).AddField(inline: false, name: "Zones", value:
            "This bot accepts zone names from the IANA Time Zone Database (a.k.a. Olson Database). " +
            "A useful tool to determine yours can be found at: https://kevinnovak.github.io/Time-Zone-Picker/"
        ).Build());
    }

    private async Task CmdList(SocketTextChannel channel, SocketGuildUser sender, SocketMessage message) {
        if (!await AreUsersDownloadedAsync(channel.Guild).ConfigureAwait(false)) {
            await channel.SendMessageAsync(ErrNoUserCache).ConfigureAwait(false);
            return;
        }

        var wspl = message.Content.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (wspl.Length == 2) {
            // Has parameter - do specific user lookup
            var usersearch = ResolveUserParameter(channel.Guild, wspl[1]);
            if (usersearch == null) {
                await channel.SendMessageAsync(":x: Cannot find the specified user.").ConfigureAwait(false);
                return;
            }

            var result = await _database.GetUserZoneAsync(usersearch).ConfigureAwait(false);
            if (result == null) {
                bool isself = sender.Id == usersearch.Id;
                if (isself) await channel.SendMessageAsync(":x: You do not have a time zone. Set it with `tz.set`.").ConfigureAwait(false);
                else await channel.SendMessageAsync(":x: The given user does not have a time zone set.").ConfigureAwait(false);
                return;
            }

            var resulttext = TzPrint(result)[4..] + ": " + FormatName(usersearch);
            await channel.SendMessageAsync(embed: new EmbedBuilder().WithDescription(resulttext).Build()).ConfigureAwait(false);
        } else {
            // Does not have parameter - build full list
            var userlist = await _database.GetGuildZonesAsync(channel.Guild.Id).ConfigureAwait(false);
            if (userlist.Count == 0) {
                await channel.SendMessageAsync(":x: Nothing to show. " +
                    $"To register time zones with the bot, use the `{CommandPrefix}set` command.").ConfigureAwait(false);
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
                    var userinstance = channel.Guild.GetUser(userid);
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
            var resultout = new StringBuilder();
            foreach (var line in outputlines) {
                if (resultout.Length + line.Length > MaxSingleOutputLength) {
                    await channel.SendMessageAsync(
                        embed: new EmbedBuilder().WithDescription(resultout.ToString()).Build()).ConfigureAwait(false);
                    resultout.Clear();
                }
                if (resultout.Length > 0) resultout.AppendLine(); // avoids trailing newline by adding to the previous line
                resultout.Append(line);
            }
            if (resultout.Length > 0) {
                await channel.SendMessageAsync(
                    embed: new EmbedBuilder().WithDescription(resultout.ToString()).Build()).ConfigureAwait(false);
            }
        }
    }

    private async Task CmdSet(SocketTextChannel channel, SocketGuildUser sender, SocketMessage message) {
        var wspl = message.Content.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (wspl.Length == 1) {
            await channel.SendMessageAsync(":x: Zone parameter is required.").ConfigureAwait(false);
            return;
        }
        var input = ParseTimeZone(wspl[1]);
        if (input == null) {
            await channel.SendMessageAsync(ErrInvalidZone).ConfigureAwait(false);
            return;
        }
        await _database.UpdateUserAsync(sender, input).ConfigureAwait(false);
        await channel.SendMessageAsync($":white_check_mark: Your time zone has been set to **{input}**.").ConfigureAwait(false);
    }

    private async Task CmdSetFor(SocketTextChannel channel, SocketGuildUser sender, SocketMessage message) {
        if (!IsUserAdmin(sender)) return;

        // Parameters: command, target, zone
        var wspl = message.Content.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (wspl.Length == 1) {
            await channel.SendMessageAsync(":x: You must specify a user to set the time zone for.").ConfigureAwait(false);
            return;
        }
        if (wspl.Length == 2) {
            await channel.SendMessageAsync(":x: You must specify a time zone to apply to the user.").ConfigureAwait(false);
            return;
        }

        if (!await AreUsersDownloadedAsync(channel.Guild).ConfigureAwait(false)) {
            await channel.SendMessageAsync(ErrNoUserCache).ConfigureAwait(false);
            return;
        }
        var targetuser = ResolveUserParameter(channel.Guild, wspl[1]);
        if (targetuser == null) {
            await channel.SendMessageAsync(ErrTargetUserNotFound).ConfigureAwait(false);
            return;
        }
        var newtz = ParseTimeZone(wspl[2]);
        if (newtz == null) {
            await channel.SendMessageAsync(ErrInvalidZone).ConfigureAwait(false);
            return;
        }

        await _database.UpdateUserAsync(targetuser, newtz).ConfigureAwait(false);
        await channel.SendMessageAsync($":white_check_mark: Time zone for **{targetuser}** set to **{newtz}**.").ConfigureAwait(false);
    }

    private async Task CmdRemove(SocketTextChannel channel, SocketGuildUser sender, SocketMessage message) {
        var success = await _database.DeleteUserAsync(sender).ConfigureAwait(false);
        if (success) await channel.SendMessageAsync(":white_check_mark: Your zone has been removed.").ConfigureAwait(false);
        else await channel.SendMessageAsync(":x: You don't have a time zone set.");
    }

    private async Task CmdRemoveFor(SocketTextChannel channel, SocketGuildUser sender, SocketMessage message) {
        if (!IsUserAdmin(sender)) return;

        // Parameters: command, target
        var wspl = message.Content.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (wspl.Length == 1) {
            await channel.SendMessageAsync(":x: You must specify a user for whom to remove time zone data.").ConfigureAwait(false);
            return;
        }

        if (!await AreUsersDownloadedAsync(channel.Guild).ConfigureAwait(false)) {
            await channel.SendMessageAsync(ErrNoUserCache).ConfigureAwait(false);
            return;
        }
        var targetuser = ResolveUserParameter(channel.Guild, wspl[1]);
        if (targetuser == null) {
            await channel.SendMessageAsync(ErrTargetUserNotFound).ConfigureAwait(false);
            return;
        }

        await _database.DeleteUserAsync(targetuser).ConfigureAwait(false);
        await channel.SendMessageAsync($":white_check_mark: Removed zone information for {targetuser}.");
    }

    /// <summary>
    /// Given parameter input, attempts to find the corresponding SocketGuildUser.
    /// </summary>
    private static SocketGuildUser? ResolveUserParameter(SocketGuild guild, string input) {
        // Try interpreting as ID
        var match = _userMention.Match(input);
        string idcheckstr = match.Success ? match.Groups[1].Value : input;
        if (ulong.TryParse(idcheckstr, out var value)) return guild.GetUser(value);

        // Prepare if input looks like Username#Discriminator
        var @explicit = _userExplicit.Match(input);

        foreach (var user in guild.Users) {
            // Explicit match search
            if (@explicit.Success) {
                var username = @explicit.Groups[1].Value;
                var discriminator = @explicit.Groups[2].Value;
                if (string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase) && user.Discriminator == discriminator)
                    return user;
            }

            // Nickname search
            if (user.Nickname != null && string.Equals(user.Nickname, input, StringComparison.OrdinalIgnoreCase)) return user;

            // Username search
            if (string.Equals(user.Username, input, StringComparison.OrdinalIgnoreCase)) return user;
        }

        return null;
    }
}
