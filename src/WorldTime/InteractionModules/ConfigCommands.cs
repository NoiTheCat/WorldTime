using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using static WorldTime.Localization.CommandsEnUS.Config;

namespace WorldTime.InteractionModules;

[Group(Name, Description)]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[CommandContextType(InteractionContextType.Guild)]
public class ConfigCommands : WTModuleBase {
    [SlashCommand(Use12hour.Name, Use12hour.Description)]
    public async Task Cmd12Hour([Summary(description: Use12hour.Setting.Description)] bool setting) {
        var gs = await GetGuildConfAsync().ConfigureAwait(false);
        gs.Use12HourTime = setting;
        await DbContext.SaveChangesAsync().ConfigureAwait(false);
        if (gs.EphemeralConfirm) {
            await RespondAsync(LRu("config.use-12hour.confirm",
                setting ? LRu("config.use-12hour.enable") : LRu("config.use-12hour.disable")), ephemeral: true)
                .ConfigureAwait(false);
        } else {
            await RespondAsync(LRg("config.use-12hour.confirm",
                setting ? LRg("config.use-12hour.enable") : LRg("config.use-12hour.disable")))
                .ConfigureAwait(false);
        }
    }

    [SlashCommand(PrivateConfirms.Name, PrivateConfirms.Description)]
    public async Task PrivateConfirmations([Summary(description: PrivateConfirms.Setting.Description)] bool setting) {
        var gs = await GetGuildConfAsync();
        gs.EphemeralConfirm = setting;
        await DbContext.SaveChangesAsync().ConfigureAwait(false);
        await RespondAsync(LRg("config.private-confirms.confirm",
                setting ? LRg("config.private-confirms.enable") : LRg("config.private-confirmsdisable")))
                .ConfigureAwait(false); // Always show this confirmation despite setting
    }

    [SlashCommand(SetFor.Name, SetFor.Description)]
    public async Task CmdSetFor([Summary(description: SetFor.User.Description)] SocketGuildUser user,
                                [Summary(description: SetFor.Zone.Description), Autocomplete<TzAutocompleteHandler>] string zone) {
        var cu = Cache.Update(user);
        
        var newtz = ParseTimeZone(zone);
        if (newtz == null) {
            if (await IsConfEphemeralConfEnableAsync().ConfigureAwait(false)) {
                await RespondAsync(LRu("errParseZone"), ephemeral: true).ConfigureAwait(false);
            } else {
                await RespondAsync(LRg("errParseZone")).ConfigureAwait(false);
            }
            return;
        }

        await UpdateDbUserAsync(user, newtz).ConfigureAwait(false);
        await RespondAsync(LRg("config.set-for", cu.FormatName(), newtz)).ConfigureAwait(false);
    }

    [SlashCommand(RemoveFor.Name, RemoveFor.Description)]
    public async Task CmdRemoveFor([Summary(description: RemoveFor.User.Description)] SocketGuildUser user) {
        var cu = Cache.Update(user);

        if (await DeleteDbUserAsync(user).ConfigureAwait(false)) {
            await RespondAsync(LRg("config.remove-for.success", cu.FormatName())).ConfigureAwait(false);
        } else {
            if (await IsConfEphemeralConfEnableAsync().ConfigureAwait(false)) {
                await RespondAsync(LRg("config.remove-for.notExist", cu.FormatName()), ephemeral: true).ConfigureAwait(false);
            } else {
                await RespondAsync(LRu("config.remove-for.notExist", cu.FormatName())).ConfigureAwait(false);
            }
        }
    }
}
