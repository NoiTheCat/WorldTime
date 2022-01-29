using NodaTime;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace WorldTime;

internal abstract class CommandsCommon {
    protected readonly Database _database;
    protected readonly WorldTime _instance;

    private static readonly ReadOnlyDictionary<string, string> _tzNameMap;

    protected const string ErrInvalidZone = ":x: Not a valid zone name."
        + " To find your time zone, refer to: <https://kevinnovak.github.io/Time-Zone-Picker/>.";
    protected const string ErrTargetUserNotFound = ":x: Unable to find the target user.";
    protected const string ErrNoUserCache = ":warning: Please try the command again.";
    protected const int MaxSingleLineLength = 750;
    protected const int MaxSingleOutputLength = 900;

    static CommandsCommon() {
        Dictionary<string, string> tzNameMap = new(StringComparer.OrdinalIgnoreCase);
        foreach (var name in DateTimeZoneProviders.Tzdb.Ids) tzNameMap.Add(name, name);
        _tzNameMap = new(tzNameMap);
    }

    public CommandsCommon(Database database, WorldTime instance) {
        _database = database;
        _instance = instance;
    }

    /// <summary>
    /// Returns a string displaying the current time in the given time zone.
    /// The result begins with four numbers for sorting purposes. Must be trimmed before output.
    /// </summary>
    protected static string TzPrint(string zone) {
        var tzdb = DateTimeZoneProviders.Tzdb;
        DateTimeZone tz = tzdb.GetZoneOrNull(zone)!;
        if (tz == null) throw new Exception("Encountered unknown time zone: " + zone);

        var now = SystemClock.Instance.GetCurrentInstant().InZone(tz);
        var sortpfx = now.ToString("MMdd", DateTimeFormatInfo.InvariantInfo);
        var fullstr = now.ToString("dd'-'MMM' 'HH':'mm' 'x' (UTC'o<g>')'", DateTimeFormatInfo.InvariantInfo);
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
                if (c is '\\' or '_' or '~' or '*' or '@') {
                    result.Append('\\');
                }
                result.Append(c);
            }
            return result.ToString();
        }

        var username = escapeFormattingCharacters(user.Username);
        if (user.Nickname != null) {
            return $"**{escapeFormattingCharacters(user.Nickname)}** ({username}#{user.Discriminator})";
        }
        return $"**{username}**#{user.Discriminator}";
    }

    /// <summary>
    /// Checks if the given user can be considered a guild admin ('Manage Server' is set).
    /// </summary>
    protected static bool IsUserAdmin(SocketGuildUser user)
        => user.GuildPermissions.Administrator || user.GuildPermissions.ManageGuild;
    // TODO port modrole feature from BB, implement in here

    /// <summary>
    /// Checks if the member cache for the specified guild needs to be filled, and sends a request if needed.
    /// </summary>
    /// <remarks>
    /// Due to a quirk in Discord.Net, the user cache cannot be filled until the command handler is no longer executing,
    /// regardless of if the request runs on its own thread.
    /// </remarks>
    /// <returns>
    /// True if the guild's members are already downloaded. If false, the command handler must notify the user.
    /// </returns>
    protected static async Task<bool> AreUsersDownloadedAsync(SocketGuild guild) {
        if (HasMostMembersDownloaded(guild)) return true;
        else {
            // Event handler hangs if awaited normally or used with Task.Run
            await Task.Factory.StartNew(guild.DownloadUsersAsync).ConfigureAwait(false);
            return false;
        }
    }

    /// <summary>
    /// An alternative to <see cref="SocketGuild.HasAllMembers"/>.
    /// Returns true if *most* members have been downloaded.
    /// Used as a workaround check due to Discord.Net occasionally unable to actually download all members.
    /// </summary>
    /// <remarks>Copied directly from BirthdayBot. Try to coordinate changes between projects...</remarks>
    private static bool HasMostMembersDownloaded(SocketGuild guild) {
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
}
