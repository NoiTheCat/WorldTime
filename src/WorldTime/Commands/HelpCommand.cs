using Discord.Interactions;

namespace WorldTime.Commands;

public class HelpCommand : CommandsBase {
    internal const string HelpHelp = "Displays a list of available bot commands.";
    internal const string HelpList = "Shows the current time for all recently active known users.";
    internal const string HelpSet = "Adds or updates your time zone to the bot.";
    internal const string HelpRemove = "Removes your time zone information from this bot.";

    [SlashCommand("help", HelpHelp)]
    [CommandContextType(InteractionContextType.Guild, InteractionContextType.BotDm)]
    public async Task CmdHelp() {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
        var guildct = Shard.GetTotalGuildCount();
        var uniquetz = GetDistinctZoneCount();
        await RespondAsync(embed: new EmbedBuilder() {
            Title = "Help & About",
            // TODO potential bug - if multiple instances run this bot, guild counts will become inaccurate. consider removal
            Description =
                $"World Time v{version} - Serving {guildct} communities across {uniquetz} time zones.\n\n"
                + "This bot is provided for free, without any paywalled 'premium' features. "
                + "If you've found this bot useful, please consider contributing via the "
                + "bot author's page on Ko-fi: https://ko-fi.com/noithecat.",
            Footer = new EmbedFooterBuilder() {
                IconUrl = Context.Client.CurrentUser.GetAvatarUrl(),
                Text = "World Time"
            }
        }.AddField(inline: false, name: "Commands", value:
            $"""
            `/help` - {HelpHelp}
            `/list` - {HelpList}
            `/set` - {HelpSet}
            `/remove` - {HelpRemove}
            """
        ).AddField(inline: false, name: "Admin commands", value:
            $"""
            `/config use-12hour` - {ConfigCommands.HelpUse12}
            `/config private-confirms` - {ConfigCommands.HelpPrivateConfirms}
            `/set-for` - {ConfigCommands.HelpSetFor}
            `/remove-for` - {ConfigCommands.HelpRemoveFor}
            """
        ).AddField(inline: false, name: "Zones", value:
            "This bot accepts zone names from the IANA Time Zone Database (a.k.a. Olson Database). " +
            "A useful tool to determine yours can be found at: https://zones.arilyn.cc/"
        ).Build());
    }
}