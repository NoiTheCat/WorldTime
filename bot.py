# WorldTime Discord bot

import asyncio
import traceback
import sys

import aiohttp
import discord
from discord.ext import commands, tasks

import config
from source.utils import common
from source.utils.custom_help import CustomHelpCommand

class WorldTime(commands.AutoShardedBot):
    def __init__(self, *args, **kwargs):
        """Overridden __init__ method to include custom attributes (session & colour)
        and to start our periodic report task."""

        super().__init__(*args,
                        activity=discord.Game('tz.help'),
                        command_prefix='tz.', fetch_offline_members=False,
                        help_command=CustomHelpCommand(), max_messages=None,
                        **kwargs)

        # although you might wanna remove this loop kwarg for the session creation below,
        # cause it's gonna be deprecated.
        self.session = aiohttp.ClientSession(loop=self.loop)
        self.colour = discord.Colour(4832159)

        self._faux_cache = {}

        self.periodic_report.start()
        self.update_users.start()

    @property
    def invite_url(self):
        """Since we can't use `self.user.id` if the bot isn't logged in yet..."""

        perms = discord.Permissions(
            add_reactions=True,
            attach_files=True,
            embed_links=True,
            send_messages=True,
            read_messages=True,
            read_message_history=True,
            )

        return discord.utils.oauth_url(self.user.id, permissions=perms)

    async def on_ready(self):
        """Called when the bot's internal cache is ready."""
        common.log_print('Status', f'Connected as {self.user.name} ({self.user.id})')

    async def on_command(self, ctx):
        """Called before a command is about to be invoked."""

        common.log_print('Command about to be invoked',
                        f'{ctx.guild.name}/{str(ctx.author)}: {ctx.message.content}')

    async def on_command_completion(self, ctx):
        """Called when a command has completed its invocation."""

        common.log_print('Command successfully invoked',
                        f'{ctx.guild.name}/{str(ctx.author)}: {ctx.message.content}')

    async def on_command_error(self, ctx, error):
        """Overridden on_command_error to notify users that they are missing permissions
        to run a command, the command is on cooldown, or some other issue within the
        command occurred.

        If the exception isn't handled (i.e. the command doesn't have a local error handler
        or it isn't handled by this error handler), this prints its traceback to `sys.stderr`
        and notifies the user the string representation of the exception."""

        if hasattr(ctx.command, 'on_error'):
            return

        error = getattr(error, 'original', error)

        if isinstance(error, commands.BadArgument):
            return await ctx.send(error)

        elif isinstance(error, commands.CommandNotFound):
            return  # prevent clutter

        elif isinstance(error, commands.CommandOnCooldown):
            return await ctx.send(
                f'That command is on cooldown for you, try again in {round(error.retry_after)}s.')

        elif isinstance(error, commands.MissingPermissions):
            joined_perms = ', '.join(
                f"`{perm.replace('_', ' ').replace('guild', 'server').title()}`"
                for perm in error.missing_perms)

            return await ctx.send(
                f'You are missing the {joined_perms} permission(s) to run that command.')

        elif isinstance(error, commands.MissingRequiredArgument):
            return await ctx.send(
                f'You are missing the {error.param.name} argument for that command.')

        elif isinstance(error, commands.NotOwner):
            return await ctx.send("That's an owner only command.")

        await ctx.send(f'{error.__class__.__name__}: {str(error)}')  # not handled

        common.log_print('Unhandled exception', ctx.command.qualified_name, file=sys.stderr)
        traceback.print_exception(type(error), error, error.__traceback__, file=sys.stderr)

    async def on_message(self, message):
        """Called for every message.

        This implementation waits until the bot is ready before processing messages.

        If the user is a bot, then returns.
        If the message is from a non-guild context, a message is sent to the channel
        and the message information is logged and the function returns.

        The user is then added to the cache for their activity to be updated."""

        await self.wait_until_ready()

        if message.author.bot:
            return

        if message.guild is None:
            common.log_print('Incoming DM', '{0}: {1}'.format(message.author,
                                                              message.content.replace('\n', '\\n'))
                                                              )
            return await message.channel.send("I don't work in DMs, only in a server!")
            # Having a return should be fine here, we don't want to process commands in a DM context.

        try:
            self._faux_cache[message.guild.id].add(message.author.id)
        except KeyError:
            self._faux_cache[message.guild.id] = {message.author.id}

        await self.process_commands(message)  # This is necessary.

    async def close(self):
        """Overridden close to cancel our periodic report task, close the database connection
        and our aiohttp ClientSession."""

        await self.db.close()
        await self.session.close()

        self.periodic_report.cancel()
        self.update_users.cancel()

        self._faux_cache = {}

        await super().close()

    @tasks.loop(seconds=21600)
    async def periodic_report(self):
        """Provides a periodic update in console of how many guilds we're in.
        Reports a guild count to Discord Bots if the appropriate token has been defined."""

        await self.wait_until_ready()

        guilds = len(self.guilds)
        common.log_print("Report", f"Currently in {guilds} guild(s).")

        try:
            authtoken = config.DBotsApiKey
        except AttributeError:
            print('Bot has no Discord Bots API key, aborting API request.')
            return

        json = {
            "guildCount": guilds
            }

        headers = {
            "Content-Type": "application/json",
            "Authorization": authtoken
            }

        async with self.session.request(
            'POST',
            f'https://discord.bots.gg/api/v1/bots/{self.user.id}/stats',
            json=json, headers=headers) \
                as resp:

            if 300 > resp.status >= 200:
                common.log_print('Report', 'Reported count to Discord Bots.')
                return

            if resp.status == 400:
                common.log_print('Report',
                                 'Discord Bots API report failed due to malformed HTTP request.')
                return

            if resp.status == 401:
                common.log_print('Report',
                                 'Discord Bots API report failed due to invalid authorization.')
                return

            common.log_print('Report',
                            f'Discord Bots API report HTTP status: {resp.status}')

    @periodic_report.error
    async def on_periodic_report_error(self, error):
        """Called if an exception occurs within our periodic report task."""

        common.log_print('Report', 'Periodic report task failed', file=sys.stderr)
        traceback.print_exception(type(error), error, error.__traceback__, file=sys.stderr)

    @tasks.loop(minutes=1)
    async def update_users(self):
        """A backround task that updates users' activity every minute."""

        await self.wait_until_ready()

        for guild_id, user_id_set in self._faux_cache.items():
            for user_id in user_id_set:
                await self.userdb.update_activity(guild_id, user_id)

        common.log_print('Report', 'Successfully updated active users.')

        self._faux_cache = {}

    @update_users.error
    async def on_update_users_error(self, error):
        """Called if an exception occurs within our user update task."""

        common.log_print('Report', 'Update users task failed', file=sys.stderr)
        traceback.print_exception(type(error), error, error.__traceback__, file=sys.stderr)

    def run(self, *args, **kwargs):
        """Why not override run so that we don't have to access the config
        token in another file?"""

        return super().run(config.bot_token, **kwargs)