using Discord.Interactions;
using NodaTime;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
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

    [NotNull]
    public ShardInstance? Shard { get; set; }
    [NotNull]
    public BotDatabaseContext? DbContext { get; set; }

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

    /// <summary>
    /// Formats a user's name to a consistent, readable format which makes use of their nickname.
    /// </summary>
    protected static string FormatName(SocketGuildUser user) {
        static string escapeFormattingCharacters(string input) {
            var result = new StringBuilder();
            foreach (var c in input) {
                if (c is '\\' or '_' or '~' or '*' or '@' or '`') {
                    result.Append('\\');
                }
                result.Append(c);
            }
            return result.ToString();
        }

        if (user.DiscriminatorValue == 0) {
            var username = escapeFormattingCharacters(user.GlobalName ?? user.Username);
            if (user.Nickname != null) {
                return $"{escapeFormattingCharacters(user.Nickname)} ({username})";
            }
            return username;
        } else {
            var username = escapeFormattingCharacters(user.Username);
            if (user.Nickname != null) {
                return $"{escapeFormattingCharacters(user.Nickname)} ({username}#{user.Discriminator})";
            }
            return $"{username}#{user.Discriminator}";
        }
    }

    /// <summary>
    /// Checks if the member cache for the specified guild needs to be filled, and sends a request if needed.
    /// </summary>
    /// <remarks>
    /// Due to a quirk in Discord.Net, the user cache cannot be filled until the command handler is no longer executing
    /// regardless of if the request runs on its own thread, thus requiring the user to run the command again.
    /// </remarks>
    /// <returns>
    /// True if the guild's members are already downloaded. If false, the command handler must notify the user.
    /// </returns>
    protected static async Task<bool> AreUsersDownloadedAsync(SocketGuild guild) {
        static bool HasMostMembersDownloaded(SocketGuild guild) {
            if (guild.HasAllMembers) return true;
            if (guild.MemberCount > 30) {
                // For guilds of size over 30, require 85% or more of the members to be known
                // (26/30, 42/50, 255/300, etc)
                return guild.DownloadedMemberCount >= (int)(guild.MemberCount * 0.85);
            } else {
                // For smaller guilds, fail if two or more members are missing
                return guild.MemberCount - guild.DownloadedMemberCount <= 2;
            }
        }
        if (HasMostMembersDownloaded(guild)) return true;
        else {
            // Event handler hangs if awaited normally or used with Task.Run
            await Task.Factory.StartNew(guild.DownloadUsersAsync).ConfigureAwait(false);
            return false;
        }
    }
}