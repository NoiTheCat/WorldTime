# WorldTime Discord bot

import asyncio
import traceback

import aiohttp
import discord
from discord.ext import commands, tasks

import config
from source.utils import common
from source.utils.custom_help import CustomHelpCommand
from source.utils.userdb import DatabaseUtil

EXTENSIONS = ['source.commands']

class WorldTime(commands.AutoShardedBot):
    def __init__(self, *args, **kwargs):
        """Overridden __init__ method to include custom attributes
        and to start our periodic report task."""

        super().__init__(*args,
                        activity=discord.Game('tz.help'),
                        command_prefix='tz.', fetch_offline_members=False,
                        help_command=CustomHelpCommand(), max_messages=None,
                        **kwargs)

        # although you might wanna remove this loop kwarg for the session creation below,
        # cause theoretically it's gonna be deprecated.
        self.session = aiohttp.ClientSession(loop=self.loop)

        self.periodic_report.start()

        for extension in EXTENSIONS:
            try:
                self.load_extension(extension)
                print(f'[EXTENSION] {extension} was loaded successfully!')
            except Exception as e:
                print(f'[WARNING] Could not load extension {extension}: {e}')

    async def on_ready(self):
        """Called when the bot's internal cache is ready."""
        common.log_print('Status', f'Connected as {self.user.name} ({self.user.id})')

    async def on_command(self, context):
        """Called before a command is about to be invoked."""

        common.log_print('Command about to be invoked',
                        f'{ctx.guild.name}/{str(ctx.author)}: {ctx.message.content}')

    async def on_command_completion(self, context):
        """Called when a command has completed its invocation."""

        common.log_print('Command successfully invoked',
                        f'{ctx.guild.name}/{str(ctx.author)}: {ctx.message.content}')

    async def on_message(self, message):
        """Called for every message.

        If the user is a bot, then returns.
        If the message is from a non-guild context, a message is sent to the channel
        and the message information is logged."""

        if message.author.bot:
            return

        if message.guild is None:
            common.log_print('Incoming DM', '{0}: {1}'.format(message.author,
                                                              message.content.replace('\n', '\\n')))
            await message.channel.send("I don't work in DMs, only in a server!")

        # Consider using a cache here of some sorts instead of updating the actual
        # database on every message. On high traffic guilds this could become a problem.

        await self.userdb.update_activity(message.guild.id, message.author.id)

    async def close(self):
        """Overridden close to cancel our periodic report task. and close the database
        connection."""

        await self.db.close()
        self.periodic_report.cancel()

        return await super().close()

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
            return

        json = {
            "guildCount": guilds}

        headers = {
            "Content-Type": "application/json",
            "Authorization": authtoken
            }

        async with self.session.request('POST',
                                        f'https://discord.bots.gg/api/v1/bots/{self.user.id}/stats',
                                        json=json, headers=headers) as resp:

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
    async def on_periodic_report_error(self, exc):
        """Called if an exception occurs within our periodic report task."""
        traceback.print_exc()

    def run(self):
        """Overridden run to initialize our database connection pool and userdb utility."""

        self.db = await asyncpg.create_pool(config.pg_uri)
        self.userdb = DatabaseUtil(self.db)

        super().run(config.bot_token)