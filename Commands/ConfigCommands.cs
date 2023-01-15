using Discord.Interactions;

namespace WorldTime.Commands;
[Group("config", HelpSettings)]
public class ConfigCommands : CommandsBase {
    internal const string HelpSettings = "Configuration commands for World Time.";
    internal const string HelpUse12 = "Sets whether to use the 12-hour (AM/PM) format in time zone listings.";

    [RequireGuildContext]
    [SlashCommand("use-12hour", HelpUse12)]
    public async Task Cmd12Hour([Summary(description: "True to enable, False to disable.")] bool setting) {
        if (!IsUserAdmin((SocketGuildUser)Context.User)) {
            await RespondAsync(ErrNotAllowed, ephemeral: true);
            return;
        }

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
}