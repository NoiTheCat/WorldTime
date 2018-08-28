# Command handlers

# Incoming messages that look like commands are passed into functions defined here.

from textwrap import dedent
import discord
import pytz
from datetime import datetime

from userdb import UserDatabase
from common import tzlcmap, logPrint

# All command functions are expected to have this signature:
# def cmd_NAME(self, guild: discord.Guild, channel: discord.TextChannel, author: discord.User, msgcontent: str)

def _tzPrint(zone : str):
    """
    Returns a string displaying the current time in the given time zone.
    Resulting string should be placed in a code block.
    """
    padding = ''
    now_time = datetime.now(pytz.timezone(zone))
    if len(now_time.strftime("%Z")) != 4: padding = ' '
    return "{:s}{:s} | {:s}".format(now_time.strftime("%H:%M %d-%b %Z%z"), padding, zone)

def _userResolve(guild: discord.Guild, userIds: list):
    """
    Given a list with user IDs, returns a string, the second half of a
    list entry, describing the users for which a zone is represented by.
    """
    if len(userIds) == 0:
        return " -- Representing 0 users. Bug?"
    
    # Try given entries. For each entry tried, attempt to get their nickname
    # or username. Failed attempts are anonymized instead of discarded.
    # Attempt at most three entries.
    namesProcessed = 0
    namesSkipped = 0
    processedNames = []
    while namesProcessed < 3 and len(userIds) > 0:
        namesProcessed += 1
        uid = userIds.pop()
        mem = guild.get_member(int(uid))
        if mem is not None:
            processedNames.append(mem.display_name)
        else:
            namesSkipped += 1
    leftovers = namesSkipped + len(userIds)
    if len(processedNames) == 0:
        return " -- Representing {0} user{1}.".format(leftovers, "s" if leftovers != 1 else "")
    result = " -- Representing "
    while len(processedNames) > 0:
        result += processedNames.pop() + ", "
    if leftovers != 0:
        result += "{0} other user{1}.".format(leftovers, "s" if leftovers != 1 else "")
    else:
        result = result[:-2] + "."
    return result

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
            resultstr = '```\n' + _tzPrint(zoneinput) + '\n```'
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
            await self._list_noparam2(guild, channel)
        else:
            await self._list_userparam(guild, channel, author, wspl[1])

    async def cmd_remove(self, guild: discord.Guild, channel: discord.TextChannel, author: discord.User, msgcontent: str):
        # To do: Check if there even is data to remove; react accordingly
        self.userdb.delete_user(guild.id, author.id)
        await channel.send(':white_check_mark: Your zone has been removed.')

    # ------
    # Supplemental command functions

    async def _list_noparam(self, guild: discord.Guild, channel: discord.TextChannel):
        # To do: improve and merge into noparam2
        clist = self.userdb.get_list(guild.id)
        if len(clist) == 0:
            await channel.send(':x: No users with known zones have been active in the last 72 hours.')
            return
        resultarr = []
        for i in clist:
            resultarr.append(_tzPrint(i))
        resultarr.sort()
        resultstr = '```\n'
        for i in resultarr:
            resultstr += i + '\n'
        resultstr += '```'
        await channel.send(resultstr)

    async def _list_noparam2(self, guild: discord.Guild, channel: discord.TextChannel):
        rawlist = self.userdb.get_list2(guild.id)
        if len(rawlist) == 0:
            await channel.send(':x: No users with known zones have been active in the last 72 hours.')
            return
            
        resultData = []
        for key, value in rawlist.items():
            resultData.append(_tzPrint(key) + '\n' + _userResolve(guild, value))
        resultData.sort()
        resultFinal = '```\n'
        for i in resultData:
            resultFinal += i + '\n'
        resultFinal += '```'
        await channel.send(resultFinal)

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
        resultstr = '```\n' + _tzPrint(res[0]) + '\n```'
        await channel.send(resultstr)