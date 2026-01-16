using System.Text;
using Discord.Interactions;
using WorldTime.Caching;

namespace WorldTime.Commands;

public class UserCommands : CommandsBase {
    [SlashCommand("list", HelpCommand.HelpList)]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task CmdList([Summary(description: "A specific user whose time to look up.")]SocketGuildUser? user = null) {
        if (user is not null) {
            Cache.Update(UserInfo.CreateFrom(user));
            // User obtained passively. Go ahead with single listing with this data.
            await CmdListWithUserParamAsync(user).ConfigureAwait(false);
            return;
        }

        var missing = GetCacheMissingUsers(Context.Guild.Id);
        if (missing.Any()) {
            // This may take a while
            await DeferAsync().ConfigureAwait(false);
            await DownloadRemainingUsersAsync(Context.Guild.Id, missing).ConfigureAwait(false);
        }
        await CmdListWithoutParamAsync();
    }

    // Guild-wide list output, called from the list command
    private async Task CmdListWithoutParamAsync() {
        const string NoResultText = ":x: Nothing to show. Register your time zones with the bot using the `/set` command.";

        // Full query replaces previous manual steps; returns timezone/user dictionary sorted by user count
        var query = DbContext.UserEntries
                .Where(e => e.GuildId == Context.Guild.Id)
                .GroupBy(e => e.TimeZone)
                .Select(e => new { e.Key, Users = e.Select(x => x.UserId).ToList() })
                .OrderByDescending(x => x.Users.Count) // TODO why? was this from back when there was a cutoff on zone results?
                .ToDictionary(x => x.Key, x => x.Users);
        var cacheusers = Cache.GetIndexedUsers(Context.Guild.Id);
        if (cacheusers == null || query.Count == 0) {
            await RespondAsync(NoResultText, ephemeral: true).ConfigureAwait(false);
            return;
        }

        const int MaxSingleLineLength = 750;
        const int MaxSingleOutputLength = 3000;
        var ampm = Is12Hour();

        // Build zone listings with users
        var outputlines = new List<string>();
        foreach (var (area, users) in query) {
            var buffer = new StringBuilder();
            buffer.Append(area[6..] + ": ");
            var empty = true;
            foreach (var userid in users) {
                if (!cacheusers.TryGetValue(userid, out var userInfo)) continue;
                if (empty) empty = !empty;
                else buffer.Append(", ");
                var useradd = userInfo.FormatName();
                if (buffer.Length + useradd.Length > MaxSingleLineLength) {
                    buffer.Append("others...");
                    break;
                } else buffer.Append(useradd);
            }
            if (!empty) outputlines.Add(buffer.ToString());
        }

        // Prepare for output - send buffers out if they become too large
        outputlines.Sort();
        var hasOutputOneLine = false;
        // First output is shown as an interaction response, followed then as regular channel messages
        async Task doOutput(Embed msg) {
            if (!hasOutputOneLine) {
                await RespondAsync(embed: msg);
                hasOutputOneLine = true;
            } else {
                await ReplyAsync(embed: msg);
            }
        }

        var resultout = new StringBuilder();
        foreach (var line in outputlines) {
            if (resultout.Length + line.Length > MaxSingleOutputLength) {
                await doOutput(new EmbedBuilder().WithDescription(resultout.ToString()).Build());
                resultout.Clear();
            }
            if (resultout.Length > 0) resultout.AppendLine(); // avoids trailing newline by adding to the previous line
            resultout.Append(line);
        }
        if (resultout.Length > 0) {
            await doOutput(new EmbedBuilder().WithDescription(resultout.ToString()).Build());
        }
    }

    // Single user's listing output, called from the list command
    private async Task CmdListWithUserParamAsync(SocketGuildUser target) {
        var zone = DbContext.UserEntries
            .Where(e => e.GuildId == Context.Guild.Id)
            .Where(e => e.UserId == target.Id)
            .Select(e => e.TimeZone)
            .SingleOrDefault();
        if (zone == null) {
            var isself = Context.User.Id == target.Id;
            if (isself) await RespondAsync(":x: You do not have a time zone. Set it with `tz.set`.", ephemeral: true);
            else await RespondAsync(":x: The given user does not have a time zone set.", ephemeral: true);
            return;
        }

        var ampm = Is12Hour();
        var resulttext = TzPrint(zone, ampm)[6..] + ": " + Cache.GetIndexedUsers(Context.Guild.Id)![target.Id].FormatName();
        await RespondAsync(embed: new EmbedBuilder().WithDescription(resulttext).Build());
    }

    [SlashCommand("set", HelpCommand.HelpSet)]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task CmdSet([Summary(description: "The new time zone to set."), Autocomplete<TzAutocompleteHandler>]string zone) {
        var parsedzone = ParseTimeZone(zone);
        if (parsedzone == null) {
            await RespondAsync(ErrInvalidZone, ephemeral: true);
            return;
        }
        using var db = DbContext;
        await UpdateDbUserAsync((SocketGuildUser)Context.User, parsedzone);
        await RespondAsync($":white_check_mark: Your time zone has been set to **{parsedzone}**.",
            ephemeral: db.GuildSettings.Where(r => r.GuildId == Context.Guild.Id).SingleOrDefault()?.EphemeralConfirm ?? false)
            .ConfigureAwait(false);
    }

    [SlashCommand("remove", HelpCommand.HelpRemove)]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task CmdRemove() {
        using var db = DbContext;
        var success = await DeleteDbUserAsync((SocketGuildUser)Context.User);
        if (success) await RespondAsync(":white_check_mark: Your zone has been removed.",
                ephemeral: db.GuildSettings.Where(r => r.GuildId == Context.Guild.Id).SingleOrDefault()?.EphemeralConfirm ?? false)
                .ConfigureAwait(false);
        else await RespondAsync(":x: You don't have a time zone set.",
                ephemeral: db.GuildSettings.Where(r => r.GuildId == Context.Guild.Id).SingleOrDefault()?.EphemeralConfirm ?? false)
                .ConfigureAwait(false);
    }

    private bool Is12Hour() =>
        DbContext.GuildSettings
        .Where(s => s.GuildId == Context.Guild.Id)
        .SingleOrDefault()?
        .Use12HourTime ?? false;
}
