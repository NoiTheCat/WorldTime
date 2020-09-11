import textwrap

import discord
from discord.ext import commands

class CustomHelpCommand(commands.HelpCommand):
    """A customized HelpCommand."""

    def __init__(self):
        """An overridden __init__ to pass in attributes for the help command."""

        super().__init__(command_attrs={
            'help': 'Shows help for a specific command or for the whole bot.'})

    def show_full_help(self) -> discord.Embed:
        """A function that returns an embed containing full help for the bot."""

        tzcount = await self.context.bot.userdb.get_unique_tz_count()

        embed = discord.Embed(
            color=14742263,
            title='Help & About',
            description=textwrap.dedent('''
                World Time v{0}
                Serving {1} communities across {2} time zones.
            '''.format(BotVersion, len(self.context.bot.guilds), tzcount))
        )

        raw_commands = sorted(self.context.bot.commands, key=lambda cmd: cmd.name)

        values = [f"`{cmd.qualified_name} {cmd.usage or ''}` - {cmd.short_doc}"
                  for cmd in raw_commands[:5]]

        embed.add_field(name='Commands', value='\n'.join(values))

        embed.add_field(
            name='Zones',
            value=textwrap.dedent("""
            This bot uses zone names from the tz database. Most common zones are supported. For a list of entries, see the "TZ database name" column under
            https://en.wikipedia.org/wiki/List_of_tz_database_time_zones.
        """), inline=False)

        embed.set_footer(text='World Time', icon_url=self.context.bot.user.avatar_url)

        return embed

    async def send_bot_help(self, mapping):
        # Just disregard mapping here, we don't really need it
        """Sends help for the entire bot."""

        embed = self.show_full_help()
        await self.context.send(embed=embed)

    async def show_help_for(self, command) -> discord.Embed:
        """Shows help for either a command or group."""

        embed = discord.Embed(
            color=14742263,
            title=f'{command.qualified_name} {command.usage or ""}',
            description=command.help or 'No help given'
            )

        if isinstance(command, commands.Group) and len(command.commands) > 0:
            embed.add_field(
                name='Subcommands',
                value=', '.join(sorted(f'`{x.name}`' for x in command.commands))
                )

        if command.aliases:
            embed.set_footer(text=f'Aliases: {", ".join(command.aliases)}')

        return embed

    async def send_command_help(self, command):
        """Sends help for a command."""

        embed = await self.show_help_for(command)
        await self.context.send(embed=embed)

    async def send_group_help(self, group):
        """Sends help for a command group."""

        embed = await self.show_help_for(group)
        await self.context.send(embed=embed)

    async def send_cog_help(self, cog):
        """We don't want to show help for a specific cog, so we instead
        show help for the whole bot."""

        embed = self.show_full_help()
        await self.context.send(embed=embed)
