using System.Globalization;
using System.Text;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using static WorldTime.Localization.CommandsEnUS;

namespace WorldTime.InteractionModules;

[CommandContextType(InteractionContextType.Guild)]
public class UserCommands : WTModuleBase {
    [SlashCommand(List.Name, List.Description)]
    public async Task CmdList([Summary(description: List.User.Description)] SocketGuildUser? user = null) {
        if (user is not null) {
            Cache.Update(user);
            // User obtained passively. Go ahead with single listing with this data.
            await CmdListWithUserParamAsync(user).ConfigureAwait(false);
            return;
        }

        var isDeferred = false;
        var refresh = Cache.RequestGuildRefreshAsync(DbContext, Context.Guild.Id, ModuleConfig.FilterAllMissing);
        if (!refresh.IsCompleted) {
            // This may take a while
            isDeferred = true;
            await RespondAsync(LRg("loadingUsers")).ConfigureAwait(false);
            await refresh.ConfigureAwait(false);
        }
        await CmdListWithoutParamAsync(isDeferred).ConfigureAwait(false);
    }

    // Guild-wide list output, called from the list command
    private async Task CmdListWithoutParamAsync(bool isDeferred) {
        var is12hour = await IsConf12HrEnableAsync().ConfigureAwait(false);
        // Create dictionary of timezone -> users
        var sortedUsers = (await DbContext.UserEntries.AsNoTracking()
                .Where(e => e.GuildId == Context.Guild.Id)
                .GroupBy(e => e.TimeZone)
                .Select(e => new { e.Key, Users = e.Select(x => x.UserId).ToList() })
                .ToListAsync().ConfigureAwait(false)) // database query done; processing becomes client-side
                .Select(o => (Area: TzPrint(o.Key, is12hour), o.Users))
                .GroupBy(g => g.Area)
                .Select(e => (Area: e.Key, Subscribers: e.SelectMany(u => u.Users).Shuffle()))
                .OrderBy(x => x.Area)
                .ToList();
        var cacheusers = Cache.GetGuild(Context.Guild.Id);
        if (cacheusers == null || sortedUsers.Count == 0)
        {
            if (isDeferred) await ModifyOriginalResponseAsync(response => response.Content = LRg("list.fullErrNoResults"));
            else await RespondAsync(LRu("fullErrNoResults"), ephemeral: true).ConfigureAwait(false);
            return;
        }

        const int MaxSingleLineLength = 750;
        const int MaxSingleOutputLength = 3000;

        // Build zone listings with users
        var outputlines = new List<string>();
        foreach (var (Area, Users) in sortedUsers) {
            var buffer = new StringBuilder();
            buffer.Append(Area[6..] + ": ");
            var empty = true;
            foreach (var userid in Users) {
                if (!cacheusers.TryGetValue(userid, out var userInfo)) continue;
                if (empty) empty = !empty;
                else buffer.Append(", ");
                var useradd = userInfo.FormatName();
                if (buffer.Length + useradd.Length > MaxSingleLineLength) {
                    buffer.Append(LRg("list.truncatedLineEnding"));
                    break;
                } else buffer.Append(useradd);
            }
            if (!empty) outputlines.Add(buffer.ToString());
        }

        // Prepare for output - send buffers out if they become too large
        outputlines.Sort();
        var useFollowup = false;
        // First output is shown as an interaction response, followed then as regular channel messages
        Task OutputAsync(Embed msg) {
            if (!useFollowup) {
                useFollowup = true;
                if (isDeferred) return ModifyOriginalResponseAsync(response => {
                    response.Content = "";
                    response.Embed = msg;
                });
                else return RespondAsync(embed: msg);
            } else {
                return FollowupAsync(embed: msg);
            }
        }

        var resultout = new StringBuilder();
        foreach (var line in outputlines)
        {
            if (resultout.Length + line.Length > MaxSingleOutputLength)
            {
                await OutputAsync(new EmbedBuilder()
                    .WithDescription(resultout.ToString())
                    .Build()).ConfigureAwait(false);
                resultout.Clear();
            }
            if (resultout.Length > 0) resultout.AppendLine(); // avoids trailing newline by adding to the previous line
            resultout.Append(line);
        }
        if (resultout.Length > 0)
        {
            await OutputAsync(new EmbedBuilder()
                .WithDescription(resultout.ToString())
                .Build()).ConfigureAwait(false);
        }
    }

    // Single user's listing output, called from the list command
    private async Task CmdListWithUserParamAsync(SocketGuildUser target)
    {
        var zone = await DbContext.UserEntries
            .Where(e => e.GuildId == Context.Guild.Id)
            .Where(e => e.UserId == target.Id)
            .Select(e => e.TimeZone)
            .AsAsyncEnumerable()
            .SingleOrDefaultAsync().ConfigureAwait(false);
        if (zone == null)
        {
            var isself = Context.User.Id == target.Id;
            if (isself) await RespondAsync(LRu("list.sg1pErrNoResult"), ephemeral: true);
            else await RespondAsync(LRu("list.sg3pErrNoResult"), ephemeral: true);
            return;
        }

        var resulttext = TzPrint(zone, await IsConf12HrEnableAsync().ConfigureAwait(false))[6..]
            + ": " + Cache.GetGuild(Context.Guild.Id)![target.Id].FormatName();
        await RespondAsync(embed: new EmbedBuilder().WithDescription(resulttext).Build());
    }

    [SlashCommand(Set.Name, Set.Description)]
    public async Task CmdSet([Summary(description: Set.Zone.Description), Autocomplete<TzAutocompleteHandler>] string zone) {
        var parsedzone = ParseTimeZone(zone);
        if (parsedzone == null)
        {
            if (await IsConfEphemeralConfEnableAsync().ConfigureAwait(false))
            {
                await RespondAsync(LRu("errParseZone"), ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                await RespondAsync(LRg("errParseZone")).ConfigureAwait(false);
            }
            return;
        }

        await UpdateDbUserAsync((SocketGuildUser)Context.User, parsedzone);
        if (await IsConfEphemeralConfEnableAsync().ConfigureAwait(false))
        {
            await RespondAsync(LRu("set", parsedzone), ephemeral: true).ConfigureAwait(false);
        }
        else
        {
            await RespondAsync(LRg("set", parsedzone)).ConfigureAwait(false);
        }
    }

    [SlashCommand(Remove.Name, Remove.Description)]
    public async Task CmdRemove() {
        var success = await DeleteDbUserAsync((SocketGuildUser)Context.User);

        if (await IsConfEphemeralConfEnableAsync().ConfigureAwait(false)) {
            await RespondAsync(success ? LRu("remove.success") : LRu("remove.notExist"), ephemeral: true).ConfigureAwait(false);
        } else {
            await RespondAsync(success ? LRg("remove.success") : LRg("remove.notExist")).ConfigureAwait(false);
        }
    }

    private AsyncLazy<bool>? _ampm;
    private Task<bool> IsConf12HrEnableAsync()
    {
        _ampm ??= new(async () => (await DbContext.GuildSettings
                .Where(s => s.GuildId == Context.Guild.Id)
                .Select(s => (bool?)s.Use12HourTime)
                .AsAsyncEnumerable()
                .SingleOrDefaultAsync().ConfigureAwait(false)) ?? false);
        return _ampm.Task;
    }

    /// <summary>
    /// Returns a string displaying the current time in the given time zone.
    /// The result begins with six numbers for sorting purposes. Must be trimmed before output.
    /// </summary>
    private string TzPrint(DateTimeZone tz, bool use12HourTime)
    {
        // TODO use localization info?
        var now = SystemClock.Instance.GetCurrentInstant().InZone(tz);
        var sortpfx = now.ToString("MMddHH", DateTimeFormatInfo.InvariantInfo);
        string fullstr;
        if (use12HourTime)
        {
            var ap = now.ToString("tt", DateTimeFormatInfo.InvariantInfo).ToLowerInvariant();
            fullstr = now.ToString($"MMM' 'dd', 'hh':'mm'{ap} 'x' (UTC'o<g>')'", DateTimeFormatInfo.InvariantInfo);
        }
        else
        {
            fullstr = now.ToString("dd'-'MMM', 'HH':'mm' 'x' (UTC'o<g>')'", DateTimeFormatInfo.InvariantInfo);
        }
        return $"{sortpfx}● `{fullstr}`";
    }
}
