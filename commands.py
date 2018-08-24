# Command handlers

# Incoming messages that look like commands are passed into functions defined here.

from textwrap import dedent
import discord

from userdb import UserDatabase
from common import tzlcmap, tzPrint, logPrint

# All command functions are expected to have this signature:
# def cmd_NAME(self, guild: discord.Guild, channel: discord.TextChannel, author: discord.User, msgcontent: str)

class WtCommands:
    def __init__(self, userdb: UserDatabase, client: discord.Client):
        self.userdb = userdb
        self.dclient = client
        self.commandlist = {
            'help' : self.cmd_help,
            'list' : self.cmd_list,
            'time' : self.cmd_time,
            'set'  : self.cmd_set,
            'remove': self.cmd_remove
        }
    
    async def dispatch(self, cmdBase: str, message: discord.Message):
        try:
            command = self.commandlist[cmdBase]
        except KeyError:
            return
        logPrint('Command invoked', '{0}/{1}: tz.{2}'.format(message.guild, message.author, cmdBase))
        await command(message.guild, message.channel, message.author, message.content)

    # ------
    # Helper functions

    # ------
    # Individual command handlers

    async def cmd_help(self, guild: discord.Guild, channel: discord.TextChannel, author: discord.User, msgcontent: str):
        em = discord.Embed(
            color=14742263,
            title='Help & About',
            description='This bot aims to answer the age-old question, "What time is it for everyone here?"')
        em.set_footer(text='World Time', icon_url=self.dclient.user.avatar_url)
        em.add_field(name='Commands', value=dedent('''
            `tz.help` - This message.
            `tz.list` - Displays current times for all recently active known users.
            `tz.list [user]` - Displays the current time for the given *user*.
            `tz.time` - Displays the current time in your time zone.
            `tz.time [zone]` - Displays the current time in the given *zone*.
            `tz.set [zone]` - Registers or updates your *zone* with the bot.
            `tz.remove` - Removes your name from this bot.
        '''))
        em.add_field(name='Zones', value=dedent('''
            This bot uses zone names from the tz database. Most common zones are supported. For a list of entries, see: https://en.wikipedia.org/wiki/List_of_tz_database_time_zones.
        '''))
        await channel.send(embed=em)

    async def cmd_time(self, guild: discord.Guild, channel: discord.TextChannel, author: discord.User, msgcontent: str):
        wspl = msgcontent.split(' ', 1)
        if len(wspl) == 1:
            # No parameter. So, doing the same thing anyway...
            await self._list_userparam(guild, channel, author, author.id)
        else:
            try:
                zoneinput = tzlcmap[wspl[1].lower()]
            except KeyError:
                await channel.send(':x: Not a valid zone name.')
                return
            resultstr = '```\n' + tzPrint(zoneinput) + '\n```'
            await channel.send(resultstr)

    async def cmd_set(self, guild: discord.Guild, channel: discord.TextChannel, author: discord.User, msgcontent: str):
        wspl = msgcontent.split(' ', 1)
        if len(wspl) == 1:
            # No parameter. But it's required
            await channel.send(':x: Zone parameter is required.')
            return
        try:
            zoneinput = tzlcmap[wspl[1].lower()]
        except KeyError:
            await channel.send(':x: Not a valid zone name.')
            return
        self.userdb.update_user(guild.id, author.id, zoneinput)
        await channel.send(':white_check_mark: Your zone has been set.')

    async def cmd_list(self, guild: discord.Guild, channel: discord.TextChannel, author: discord.User, msgcontent: str):
        wspl = msgcontent.split(' ', 1)
        if len(wspl) == 1:
            await self._list_noparam(guild, channel)
        else:
            await self._list_userparam(guild, channel, author, wspl[1])

    async def cmd_remove(self, guild: discord.Guild, channel: discord.TextChannel, author: discord.User, msgcontent: str):
        # To do: Check if there even is data to remove; react accordingly
        self.userdb.delete_user(guild.id, author.id)
        await channel.send(':white_check_mark: Your zone has been removed.')

    # ------
    # Supplemental command functions

    async def _list_noparam(self, guild: discord.Guild, channel: discord.TextChannel):
        clist = self.userdb.get_list(guild.id)
        if len(clist) == 0:
            await channel.send(':x: No users with known zones have been active in the last 72 hours.')
            return
        resultarr = []
        for i in clist:
            resultarr.append(tzPrint(i))
        resultarr.sort()
        resultstr = '```\n'
        for i in resultarr:
            resultstr += i + '\n'
        resultstr += '```'
        await channel.send(resultstr)

    async def _list_userparam(self, guild: discord.Guild, channel: discord.TextChannel, author: discord.User, param):
        # wishlist: search based on username/nickname
        param = str(param)
        if param.startswith('<@!') and param.endswith('>'):
            param = param[3:][:-1]
        if param.startswith('<@') and param.endswith('>'):
            param = param[2:][:-1]
        if not param.isnumeric():
            # Didn't get an ID...
            await channel.send(':x: You must specify a user by ID or `@` mention.')
            return
        res = self.userdb.get_list(guild.id, param)
        if len(res) == 0:
            spaghetti = author.id == param
            if spaghetti: await channel.send(':x: You do not have a time zone. Set it with `tz.set`.')
            else: await channel.send(':x: The given user has not set a time zone. Ask to set it with `tz.set`.')
            return
        resultstr = '```\n' + tzPrint(res[0]) + '\n```'
        await channel.send(resultstr)