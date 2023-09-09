using Discord.Interactions;

namespace WorldTime.Commands;
[Group("config", "Configuration commands for World Time.")]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[EnabledInDm(false)]
public class ConfigCommands : CommandsBase {
    internal const string HelpUse12 = "Sets whether to use the 12-hour (AM/PM) format in time zone listings.";
    internal const string HelpSetFor = "Sets/updates time zone for a given user.";
    internal const string HelpRemoveFor = "Removes time zone for a given user.";
    internal const string HelpPrivateConfirms
        = "Sets whether to make confirmations for commands visible only to the user, except set-for and remove-for.";

    internal const string HelpBool = "True to enable, False to disable.";

    [SlashCommand("use-12hour", HelpUse12)]
    public async Task Cmd12Hour([Summary(description: HelpBool)] bool setting) {
        using var db = DbContext;
        var gs = db.GuildSettings.Where(r => r.GuildId == Context.Guild.Id).SingleOrDefault();
        if (gs == null) {
            gs = new() { GuildId = Context.Guild.Id };
            db.Add(gs);
        }

        gs.Use12HourTime = setting;
        await db.SaveChangesAsync().ConfigureAwait(false);
        await RespondAsync($":white_check_mark: Time listing set to **{(setting ? "AM/PM" : "24 hour")}** format.",
            ephemeral: gs.EphemeralConfirm).ConfigureAwait(false);
    }

    [SlashCommand("private-confirms", HelpPrivateConfirms)]
    public async Task PrivateConfirmations([Summary(description: HelpBool)] bool setting) {
        using var db = DbContext;
        var gs = db.GuildSettings.Where(r => r.GuildId == Context.Guild.Id).SingleOrDefault();
        if (gs == null) {
            gs = new() { GuildId = Context.Guild.Id };
            db.Add(gs);
        }

        gs.EphemeralConfirm = setting;
        await db.SaveChangesAsync().ConfigureAwait(false);
        await RespondAsync($":white_check_mark: Private confirmations **{(setting ? "enabled" : "disabled")}**.",
            ephemeral: false).ConfigureAwait(false); // Always show this confirmation despite setting
    }

    [SlashCommand("set-for", HelpSetFor)]
    public async Task CmdSetFor([Summary(description: "The user whose time zone to modify.")] SocketGuildUser user,
                                 [Summary(description: "The new time zone to set.")] string zone) {
        using var db = DbContext;
        // Extract parameters
        var newtz = ParseTimeZone(zone);
        if (newtz == null) {
            await RespondAsync(ErrInvalidZone,
                ephemeral: db.GuildSettings.Where(r => r.GuildId == Context.Guild.Id).SingleOrDefault()?.EphemeralConfirm ?? false)
                .ConfigureAwait(false);
            return;
        }

        db.UpdateUser(user, newtz);
        await RespondAsync($":white_check_mark: Time zone for **{user}** set to **{newtz}**.").ConfigureAwait(false);
    }

    [SlashCommand("remove-for", HelpRemoveFor)]
    public async Task CmdRemoveFor([Summary(description: "The user whose time zone to remove.")] SocketGuildUser user) {
        using var db = DbContext;
        if (db.DeleteUser(user))
            await RespondAsync($":white_check_mark: Removed zone information for {user}.").ConfigureAwait(false);
        else
            await RespondAsync($":white_check_mark: No time zone is set for {user}.",
                ephemeral: db.GuildSettings.Where(r => r.GuildId == Context.Guild.Id).SingleOrDefault()?.EphemeralConfirm ?? false)
                .ConfigureAwait(false);
    }
}