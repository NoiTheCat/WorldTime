using System.Collections.ObjectModel;
using System.Globalization;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using WorldTime.Caching;
using WorldTime.Data;

namespace WorldTime.Commands;

public class CommandsBase : InteractionModuleBase<SocketInteractionContext> {
    protected const string ErrInvalidZone =
        ":x: Not a valid zone name. To find your zone, you may refer to a site such as <https://zones.arilyn.cc/>.";
    protected const string ErrNoUserCache = ":warning: Oops, bot wasn't ready. Please try again in a moment.";

    private static readonly ReadOnlyDictionary<string, string> _tzNameMap;

    static CommandsBase() {
        Dictionary<string, string> tzNameMap = new(StringComparer.OrdinalIgnoreCase);
        foreach (var name in DateTimeZoneProviders.Tzdb.Ids) tzNameMap.Add(name, name);
        _tzNameMap = new(tzNameMap);
    }

    // Injected by DI:
    public ShardInstance Shard { get; set; } = null!;
    public BotDatabaseContext DbContext { get; set; } = null!;
    public UserCache Cache { get; set; } = null!;

    // Opportunistically caches user data coming in via interactions.
    public override Task BeforeExecuteAsync(ICommandInfo command) {
        if (Context.User is IGuildUser incoming)
            Cache.Update(UserInfo.CreateFrom(incoming));
        return base.BeforeExecuteAsync(command);
    }

    /// <summary>
    /// Returns a string displaying the current time in the given time zone.
    /// The result begins with six numbers for sorting purposes. Must be trimmed before output.
    /// </summary>
    protected static string TzPrint(string zone, bool use12hr) {
        var tzdb = DateTimeZoneProviders.Tzdb;
        DateTimeZone tz = tzdb.GetZoneOrNull(zone) ?? throw new Exception("Encountered unknown time zone: " + zone);
        var now = SystemClock.Instance.GetCurrentInstant().InZone(tz);
        var sortpfx = now.ToString("MMddHH", DateTimeFormatInfo.InvariantInfo);
        string fullstr;
        if (use12hr) {
            var ap = now.ToString("tt", DateTimeFormatInfo.InvariantInfo).ToLowerInvariant();
            fullstr = now.ToString($"MMM' 'dd', 'hh':'mm'{ap} 'x' (UTC'o<g>')'", DateTimeFormatInfo.InvariantInfo);
        } else fullstr = now.ToString("dd'-'MMM', 'HH':'mm' 'x' (UTC'o<g>')'", DateTimeFormatInfo.InvariantInfo);
        return $"{sortpfx}● `{fullstr}`";
    }

    /// <summary>
    /// Checks given time zone input. Returns a valid string for use with NodaTime, or null.
    /// </summary>
    protected static string? ParseTimeZone(string tzinput) {
        if (tzinput.Equals("Asia/Calcutta", StringComparison.OrdinalIgnoreCase)) tzinput = "Asia/Kolkata";
        if (_tzNameMap.TryGetValue(tzinput, out var name)) return name;
        return null;
    }

    protected List<ulong> GetCacheMissingUsers(ulong guildId) {
        // Simple for now - return all database IDs not in current cache
        var local = Cache.GetEntriesForGuild(guildId, false).Select(e => e.UserId);
        var remote = DbContext.UserEntries
            .Where(e => e.GuildId == guildId)
            .Select(e => e.UserId);
        return [.. remote.Except(local)];
    }

    [Obsolete("don't use this anymore")]
    protected Task DownloadRemainingUsersAsync(ulong guildId, IEnumerable<ulong> users) {
        // TODO this code is ugly - too much duplicated from UserCacheFill
        // absolutely needs to be redone. consider this a placeholder
        var _downloadGate = new SemaphoreSlim(10);
        var tasks = users.Chunk(200).First().Select(async u => {
            await _downloadGate.WaitAsync();
            try {
                await Task.Delay(Program.JitterSource.Value!.Next(50, 500));

                var incoming = await Shard.DiscordClient.Rest
                    .GetGuildUserAsync(guildId, u);
                if (incoming is not null) {
                    Cache.Update(UserInfo.CreateFrom(incoming));
                } else {
                    Cache.Update(UserInfo.NullFrom(guildId, u));
                }
            }
            finally {
                _downloadGate.Release();
            }
        });
        return Task.WhenAll(tasks);
    }

    #region Database helper methods
    /// <summary>
    /// Inserts/updates the specified user in the database.
    /// </summary>
    protected async Task UpdateDbUserAsync(SocketGuildUser user, string timezone) {
        var tuser = DbContext.UserEntries
            .Where(u => u.UserId == user.Id && u.GuildId == user.Guild.Id).SingleOrDefault();
        if (tuser == null) {
            tuser = new UserEntry() { UserId = user.Id, GuildId = user.Guild.Id };
            DbContext.Add(tuser);
        }
        tuser.TimeZone = timezone;
        await DbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Gets the number of unique time zones in the database.
    /// </summary>
    protected int GetDistinctZoneCount()
        => DbContext.UserEntries.Select(u => u.TimeZone).Distinct().Count();

    /// <summary>
    /// Removes the specified user from the database.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the removal was successful.
    /// <see langword="false"/> if the user did not exist.
    /// </returns>
    protected async Task<bool> DeleteDbUserAsync(SocketGuildUser user) {
        var tuser = DbContext.UserEntries
            .Where(u => u.UserId == user.Id && u.GuildId == user.Guild.Id).SingleOrDefault();
        if (tuser == null) return false;
        DbContext.Remove(tuser);
        await DbContext.SaveChangesAsync();
        return true;
    }

    protected GuildConfiguration GetGuildConf(ulong guildId) {
        var gs = DbContext.GuildSettings.Where(r => r.GuildId == Context.Guild.Id).SingleOrDefault();
        if (gs == null) {
            gs = new() { GuildId = Context.Guild.Id };
            DbContext.Add(gs);
        }
        return gs;
    }

    protected bool GetEphemeralConfirm()
        => DbContext.GuildSettings
            .Where(r => r.GuildId == Context.Guild.Id)
            .SingleOrDefault()?.EphemeralConfirm ?? false;
    #endregion
}
