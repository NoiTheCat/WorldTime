using Discord.Interactions;

namespace WorldTime.Commands;
[Group("config", "Configuration commands for World Time.")]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[EnabledInDm(false)]
public class ConfigCommands : CommandsBase {
    internal const string HelpUse12 = "Sets whether to use the 12-hour (AM/PM) format in time zone listings.";
    internal const string HelpSetFor = "Sets/updates time zone for a given user.";
    internal const string HelpRemoveFor = "Removes time zone for a given user.";

    [SlashCommand("use-12hour", HelpUse12)]
    public async Task Cmd12Hour([Summary(description: "True to enable, False to disable.")] bool setting) {
        using var db = DbContext;
        var gs = db.GuildSettings.Where(r => r.GuildId == Context.Guild.Id).SingleOrDefault();
        if (gs == null) {
            gs = new() { GuildId = Context.Guild.Id };
            db.Add(gs);
        }

        gs.Use12HourTime = setting;
        await db.SaveChangesAsync();
        await RespondAsync($":white_check_mark: Time listing set to **{(setting ? "AM/PM" : "24 hour")}** format.");
    }

    [SlashCommand("set-for", HelpSetFor)]
    public async Task CmdSetFor([Summary(description: "The user whose time zone to modify.")] SocketGuildUser user,
                                 [Summary(description: "The new time zone to set.")] string zone) {
        // Extract parameters
        var newtz = ParseTimeZone(zone);
        if (newtz == null) {
            await RespondAsync(ErrInvalidZone);
            return;
        }

        using var db = DbContext;
        db.UpdateUser(user, newtz);
        await RespondAsync($":white_check_mark: Time zone for **{user}** set to **{newtz}**.");
    }

    [SlashCommand("remove-for", HelpRemoveFor)]
    public async Task CmdRemoveFor([Summary(description: "The user whose time zone to remove.")] SocketGuildUser user) {
        using var db = DbContext;
        if (db.DeleteUser(user))
            await RespondAsync($":white_check_mark: Removed zone information for {user}.");
        else
            await RespondAsync($":white_check_mark: No time zone is set for {user}.");
    }
}