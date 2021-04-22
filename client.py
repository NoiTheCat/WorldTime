# WorldTime Discord client

import discord
import asyncio
import aiohttp

from common import logPrint
import settings
from userdb import UserDatabase
from commands import WtCommands

class WorldTime(discord.AutoShardedClient):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.userdb = UserDatabase(settings.PgConnectionString)
        self.commands = WtCommands(self.userdb, self)
        self.bg_task = self.loop.create_task(self.periodic_report())

    async def on_shard_connect(self, sid):
        logPrint('Status', 'Shard {2} connected as {0} ({1})'.format(self.user.name, self.user.id, sid))
        await self.change_presence(activity=discord.Game("tz.help"), shard_id=sid)
    
    async def on_message(self, message):
        # ignore bots (should therefore also ignore self)
        if message.author.bot: return

        if isinstance(message.channel, discord.DMChannel):
            await self.respond_dm(message)
            return

        # Regular message
        self.userdb.update_activity(message.guild.id, message.author.id)
        cmdBase = message.content.split(' ', 1)[0].lower()
        if cmdBase.startswith('tz.'): # wishlist: per-guild customizable prefix
            cmdBase = cmdBase[3:]
            await self.commands.dispatch(cmdBase, message)

    async def respond_dm(self, message):
        logPrint('Incoming DM', '{0}: {1}'.format(message.author, message.content.replace('\n', '\\n')))
        await message.channel.send('''I don't work over DM. Refer to the `tz.help` command when in a server.''')

    # ----------------

    async def periodic_report(self):
        '''
        Provides a periodic update in console of how many guilds we're on.
        Reports guild count to Discord Bots if the appropriate token has been defined.
        '''
        try:
            authtoken = settings.DBotsApiKey
        except AttributeError:
            authtoken = ''

        await self.wait_until_ready()
        while not self.is_closed():
            guildcount = len(self.guilds)
            async with aiohttp.ClientSession() as session:
                if authtoken != '':
                    rurl = "https://discord.bots.gg/api/v1/bots/{}/stats".format(self.user.id)
                    rdata = { "guildCount": guildcount }
                    rhead = { "Content-Type": "application/json", "Authorization": authtoken }
                    try:
                        await session.post(rurl, json=rdata, headers=rhead)
                        logPrint("Report", "Reported count to Discord Bots.")
                    except aiohttp.ClientError as e:
                        logPrint("Report", "Discord Bots API report failed: {}".format(e))
                    except Exception as e:
                        logPrint("Report", "Unknown error on Discord Bots API report.")
            logPrint("Report", "Currently in {0} guild(s).".format(guildcount))
            await asyncio.sleep(21600) # Repeat once every six hours
