using System.Collections.ObjectModel;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using NodaTime;
using NodaTime.Extensions;
using NoiPublicBot;
using NoiPublicBot.Common.UserCache;
using WorldTime.Data;
using static WorldTime.Localization.StringProviders;

namespace WorldTime.InteractionModules;

public class WTModuleBase : InteractionModuleBase<SocketInteractionContext> {
    private static readonly ReadOnlyDictionary<string, DateTimeZone> _tzNameMap;

    static WTModuleBase() {
        Dictionary<string, DateTimeZone> tzNameMap = new(StringComparer.OrdinalIgnoreCase);
        foreach (var zone in DateTimeZoneProviders.Tzdb.GetAllZones()) tzNameMap.Add(zone.Id, zone!);
        _tzNameMap = new(tzNameMap);
    }

    // Injected by DI:
    public ShardInstance Shard { get; set; } = null!;
    public BotDatabaseContext DbContext { get; set; } = null!;
    public UserCache<BotDatabaseContext> Cache { get; set; } = null!;

    // Other helpers:
    protected string GuildLocale => Context.Interaction.GuildLocale;
    protected string UserLocale => Context.Interaction.UserLocale;

    // Opportunistically caches user data coming in via interactions.
    public override Task BeforeExecuteAsync(ICommandInfo command) {
        if (Context.User is IGuildUser incoming)
            Cache.Update(incoming);
        return base.BeforeExecuteAsync(command);
    }

    /// <summary>
    /// Checks given time zone input. Returns a valid string for use with NodaTime, or null.
    /// </summary>
    protected static DateTimeZone? ParseTimeZone(string tzinput) {
        if (tzinput.Equals("Asia/Calcutta", StringComparison.OrdinalIgnoreCase)) tzinput = "Asia/Kolkata";
        if (_tzNameMap.TryGetValue(tzinput, out var name)) return name;
        return null;
    }

    #region Database helper methods
    /// <summary>
    /// Inserts/updates the specified user in the database.
    /// </summary>
    protected async Task UpdateDbUserAsync(SocketGuildUser user, DateTimeZone timezone) {
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

    protected bool HasEphemeralConfirms()
        => DbContext.GuildSettings
            .Where(r => r.GuildId == Context.Guild.Id)
            .SingleOrDefault()?.EphemeralConfirm ?? false;
    #endregion

    /// <summary>Get string from Commands using guild locale.</summary>
    protected string LCg(string key, params object?[] format) => Commands.Get(GuildLocale, key, format);
    /// <summary>Get string from Commands using user locale.</summary>
    protected string LCu(string key, params object?[] format) => Commands.Get(UserLocale, key, format);
    /// <summary>Get string from Responses using guild locale.</summary>
    protected string LRg(string key, params object?[] format) => Responses.Get(GuildLocale, key, format);
    /// <summary>Get string from Responses using user locale.</summary>
    protected string LRu(string key, params object?[] format) => Responses.Get(UserLocale, key, format);
}
