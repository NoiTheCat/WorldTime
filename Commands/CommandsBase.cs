using Discord.Interactions;
using NodaTime;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using WorldTime.Data;

namespace WorldTime.Commands;
public class CommandsBase : InteractionModuleBase<ShardedInteractionContext> {
    protected const string ErrInvalidZone = ":x: Not a valid zone name."
        + " To find your time zone, refer to: <https://kevinnovak.github.io/Time-Zone-Picker/>.";
    protected const string ErrNoUserCache = ":warning: Please try the command again.";
    protected const string ErrNotAllowed = ":x: Only server moderators may use this command.";

    private static readonly ReadOnlyDictionary<string, string> _tzNameMap;

    static CommandsBase() {
        Dictionary<string, string> tzNameMap = new(StringComparer.OrdinalIgnoreCase);
        foreach (var name in DateTimeZoneProviders.Tzdb.Ids) tzNameMap.Add(name, name);
        _tzNameMap = new(tzNameMap);
    }

    public DiscordShardedClient ShardedClient { get; set; } = null!;
    public BotDatabaseContext DbContext { get; set; } = null!;

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
        if (!_tzNameMap.TryGetValue(tzinput, out tzinput!)) return null;

        // We directly convert between -some- aliases in an effort to clean up the list display,
        // inadvertently displaying the same region in two separate areas and causing confusion
        // or otherwise replacing much longer names with shorter aliases.
        // Source: https://nodatime.org/TimeZones. Version referenced: 2022g.
        string name;
        switch (tzinput) {
            case "Iceland":
                name = "Atlantic/Reykjavik";
                break;
            case "Egypt":
                name = "Africa/Cairo";
                break;
            case "Libya":
                name = "Africa/Tripoli";
                break;
            case "America/Atka":
            case "US/Aleutian":
                name = "America/Adak";
                break;
            case "US/Alaska":
                name = "America/Anchorage";
                break;
            case "America/Argentina/Buenos_Aires":
                name = "America/Buenos_Aires";
                break;
            case "America/Argentina/Catamarca":
            case "America/Argentina/ComodRivadavia":
                name = "America/Catamarca";
                break;
            case "America/Argentina/Cordoba":
            case "America/Rosario":
                name = "America/Cordoba";
                break;
            case "America/Argentina/Jujuy":
                name = "America/Jujuy";
                break;
            case "America/Argentina/Mendoza":
                name = "America/Mendoza";
                break;
            case "US/Central":
                name = "America/Chicago";
                break;
            case "America/Shiprock":
            case "Navajo":
            case "US/Mountain":
                name = "America/Denver";
                break;
            case "US/Michigan":
                name = "America/Detroit";
                break;
            case "Canada/Mountain":
                name = "America/Edmonton";
                break;
            case "Canada/Atlantic":
                name = "America/Halifax";
                break;
            case "Cuba":
                name = "America/Havana";
                break;
            case "America/Fort_Wayne":
            case "America/Indianapolis":
            case "US/East-Indiana":
                name = "America/Indiana/Indanapolis";
                break;
            case "America/Knox_IN":
            case "US/Indiana-Starke":
                name = "America/Indiana/Knox";
                break;
            case "America/Pangnirtung":
                name = "America/Iqaluit";
                break;
            case "Jamaica":
                name = "America/Jamaica";
                break;
            case "America/Kentucky/Louisville":
                name = "America/Louisville";
                break;
            case "US/Pacific":
                name = "America/Los_Angeles";
                break;
            case "Brazil/West":
                name = "America/Manaus";
                break;
            case "Mexico/BajaSur":
                name = "America/Mazatlan";
                break;
            case "Mexico/General":
                name = "America/Mexico_City";
                break;
            case "US/Eastern":
                name = "America/New_York";
                break;
            case "Brazil/DeNoronha":
                name = "America/Noronha";
                break;
            case "America/Godthab":
                name = "America/Nuuk";
                break;
            case "America/Creston":
            case "US/Arizona":
                name = "America/Phoenix";
                break;
            case "Canada/Saskatchewan":
                name = "America/Regina";
                break;
            case "America/Porto_Acre":
            case "Brazil/Acre":
                name = "America/Rio_Branco";
                break;
            case "Chile/Continental":
                name = "America/Santiago";
                break;
            case "Brazil/East":
                name = "America/Sao_Paulo";
                break;
            case "Canada/Newfoundland":
                name = "America/St_Johns";
                break;
            case "America/Ensenada":
            case "America/Santa_Isabel":
            case "Mexico/BajaNorte":
                name = "America/Tijuana";
                break;
            case "America/Montreal":
            case "America/Nipigon":
            case "America/Thunder_Bay":
            case "Canada/Eastern":
                name = "America/Toronto";
                break;
            case "Canada/Pacific":
                name = "America/Vancouver";
                break;
            case "Canada/Yukon":
                name = "America/Whitehorse";
                break;
            case "America/Rainy_River":
            case "Canada/Central":
                name = "America/Winnipeg";
                break;
            case "Asia/Ashkhabad":
                name = "Asia/Ashgabat";
                break;
            case "Asia/Dacca":
                name = "Asia/Dhaka";
                break;
            case "Asia/Saigon":
                name = "Asia/Ho_Chi_Minh";
                break;
            case "Hongkong":
                name = "Asia/Hong_Kong";
                break;
            case "Asia/Tel_Aviv":
            case "Israel":
                name = "Asia/Jerusalem";
                break;
            case "Asia/Katmandu":
                name = "Asia/Kathmandu";
                break;
            case "Asia/Calcutta":
                name = "Asia/Kolkata";
                break;
            case "Asia/Macao":
                name = "Asia/Macau";
                break;
            case "Europe/Nicosia":
                name = "Asia/Nicosia";
                break;
            case "ROK":
                name = "Asia/Seoul";
                break;
            case "Asia/Chongqing":
            case "Asia/Chungking":
            case "Asia/Harbin":
            case "PRC":
                name = "Asia/Shanghai";
                break;
            case "Singapore":
                name = "Asia/Singapore";
                break;
            case "ROC":
                name = "Asia/Taipei";
                break;
            case "Iran":
                name = "Asia/Tehran";
                break;
            case "Asia/Thimbu":
                name = "Asia/Thimphu";
                break;
            case "Tokyo":
                name = "Asia/Tokyo";
                break;
            case "Asia/Ulan_Bator":
                name = "Asia/Ulaanbaatar";
                break;
            case "Asia/Rangoon":
                name = "Asia/Yangon";
                break;
            case "Atlantic/Faeroe":
                name = "Atlantic/Faroe";
                break;
            case "Australia/South":
                name = "Australia/Adelaide";
                break;
            case "Australia/Queensland":
                name = "Australia/Brisbane";
                break;
            case "Australia/Yancowinna":
                name = "Australia/Brorken_Hill";
                break;
            case "Australia/North":
                name = "Australia/Darwin";
                break;
            case "Australia/Currie":
            case "Australia/Tasmania":
                name = "Australia/Hobart";
                break;
            case "Australia/LHI":
                name = "Australia/Lord_Howe";
                break;
            case "Australia/Victoria":
                name = "Australia/Melbourne";
                break;
            case "Australia/West":
                name = "Australia/Perth";
                break;
            case "Australia/ACT":
            case "Australia/Canberra":
            case "Australia/NSW":
                name = "Australia/Sydney";
                break;
            case "Etc/Greenwich":
            case "Greenwich":
                name = "Etc/GMT";
                break;
            case "Etc/UCT":
            case "Etc/Universal":
            case "Etc/Zulu":
            case "UCT":
            case "UTC":
            case "Universal":
            case "Zulu":
                name = "Etc/UTC";
                break;
            case "Eire":
                name = "Europe/Dublin";
                break;
            case "Asia/Istanbul":
            case "Turkey":
                name = "Europe/Istanbul";
                break;
            case "Europe/Kiev":
            case "Europe/Uzhgorod":
            case "Europe/Zaporozhye":
                name = "Europe/Kyiv";
                break;
            case "Portugal":
                name = "Europe/Lisbon";
                break;
            case "Europe/Belfast":
            case "GB":
            case "GB-Eire":
                name = "Europe/London";
                break;
            case "W-SU":
                name = "Europe/Moscow";
                break;
            case "Poland":
                name = "Europe/Warsaw";
                break;
            case "Europe/Busingen":
                name = "Europe/Zurich";
                break;
            case "NZ":
                name = "Pacific/Auckland";
                break;
            case "NZ-CHAT":
                name = "Pacific/Chatham";
                break;
            case "Chile/EasterIsland":
                name = "Pacific/Easter";
                break;
            case "US/Hawaii":
                name = "Pacific/Honolulu";
                break;
            case "Kwajalein":
                name = "Pacific/Kwajalein";
                break;
            case "Pacific/Samoa":
            case "US/Samoa":
                name = "Pacific/Pago_Pago";
                break;
            default:
                name = tzinput;
                break;
        }
        if (!_tzNameMap.TryGetValue(name, out _)) {
            // TODO proper logging (it's not exposed, so can't do it as-is)
            Console.WriteLine($"!!! Our replacement for {tzinput} (to {name}) failed! Will use original input...");
            return tzinput;
        }
        return name;
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
    // TODO replace this with a precondition, or there's also a new permission scheme going around?
    protected static bool IsUserAdmin(SocketGuildUser user)
        => user.GuildPermissions.Administrator || user.GuildPermissions.ManageGuild;

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