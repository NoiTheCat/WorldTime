# WorldTime Discord client

import discord
import asyncio
import aiohttp

from datetime import datetime
import pytz

import settings
from userdb import UserDatabase

# For case-insensitive time zone lookup, map lowercase tzdata entries with
# entires with proper case. pytz is case sensitive.
tzlcmap = {x.lower():x for x in pytz.common_timezones}

timefmt = "%H:%M %d-%b %Z%z"
def tzPrint(zone : str):
    """
    Returns a string displaying the current time in the given time zone.
    Resulting string should be placed in a code block.
    """
    padding = ''
    now_time = datetime.now(pytz.timezone(zone))
    if len(now_time.strftime("%Z")) != 4: padding = ' '
    return "{:s}{:s} | {:s}".format(now_time.strftime(timefmt), padding, zone)

def tsPrint(label, line):
    """
    Print with timestamp in a way that resembles some of my other projects
    """
    resultstr = datetime.utcnow().strftime('%Y-%m-%d %H:%m:%S') + ' [' + label + '] ' + line
    print(resultstr)

class WorldTime(discord.Client):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.udb = UserDatabase('users.db')
        self.bg_task = self.loop.create_task(self.periodic_report())

    async def on_ready(self):
        tsPrint('Status', 'Connected as {0} ({1})'.format(self.user.name, self.user.id))
        
    # Command processing -----------------
    async def cmd_help(self, message : discord.Message):
        em = discord.Embed(
            color=14742263,
            title='Help & About',
            description='This bot aims to answer the age-old question, "What time is it for everyone here?"')
        em.set_footer(text='World Time', icon_url=self.user.avatar_url)
        em.add_field(
            name='Commands', value='''
`tz.help` - This message.
`tz.list` - Displays current times for all recently active known users.
`tz.list [user]` - Displays the current time for the given *user*.
`tz.time` - Displays the current time in your time zone.
`tz.time [zone]` - Displays the current time in the given *zone*.
`tz.set [zone]` - Registers or updates your *zone* with the bot.
`tz.remove` - Removes your name from this bot.
''')
        em.add_field(
            name='Zones', value='''
This bot uses zone names from the tz database. Most common zones are supported. For a list of entries, see: https://en.wikipedia.org/wiki/List_of_tz_database_time_zones.
            ''')
        await message.channel.send(embed=em)

    async def cmd_list(self, message):
        wspl = message.content.split(' ', 1)
        if len(wspl) == 1:
            await self.cmd_list_noparam(message)
        else:
            await self.cmd_list_userparam(message, wspl[1])

    async def cmd_list_noparam(self, message : discord.Message):
        clist = self.udb.get_list(message.guild.id)
        if len(clist) == 0:
            await message.channel.send(':x: No users with known zones have been active in the last 72 hours.')
            return
        resultarr = []
        for i in clist:
            resultarr.append(tzPrint(i))
        resultarr.sort()
        resultstr = '```\n'
        for i in resultarr:
            resultstr += i + '\n'
        resultstr += '```'
        await message.channel.send(resultstr)

    async def cmd_list_userparam(self, message, param):
        # wishlist: search based on username/nickname
        param = str(param)
        if param.startswith('<@!') and param.endswith('>'):
            param = param[3:][:-1]
        if param.startswith('<@') and param.endswith('>'):
            param = param[2:][:-1]
        if not param.isnumeric():
            # Didn't get an ID...
            await message.channel.send(':x: You must specify a user by ID or `@` mention.')
            return
        res = self.udb.get_list(message.guild.id, param)
        if len(res) == 0:
            spaghetti = message.author.id == param
            if spaghetti: await message.channel.send(':x: You do not have a time zone. Set it with `tz.set`.')
            else: await message.channel.send(':x: The given user has not set a time zone. Ask to set it with `tz.set`.')
            return
        resultstr = '```\n' + tzPrint(res[0]) + '\n```'
        await message.channel.send(resultstr)

    async def cmd_time(self, message):
        wspl = message.content.split(' ', 1)
        if len(wspl) == 1:
            # No parameter. So, doing the same thing anyway...
            await self.cmd_list_userparam(message, message.author.id)
        else:
            try:
                zoneinput = tzlcmap[wspl[1].lower()]
            except KeyError:
                await message.channel.send(':x: Not a valid zone name.')
                return
            resultstr = '```\n' + tzPrint(zoneinput) + '\n```'
            await message.channel.send(resultstr)

    async def cmd_set(self, message):
        wspl = message.content.split(' ', 1)
        if len(wspl) == 1:
            # No parameter. But it's required
            await message.channel.send(':x: Zone parameter is required.')
            return
        try:
            zoneinput = tzlcmap[wspl[1].lower()]
        except KeyError:
            await message.channel.send(':x: Not a valid zone name.')
            return
        self.udb.update_user(message.guild.id, message.author.id, zoneinput)
        await message.channel.send(':white_check_mark: Your zone has been set.')

    async def cmd_remove(self, message):
        self.udb.delete_user(message.guild.id, message.author.id)
        await message.channel.send(':white_check_mark: Your zone has been removed.')

    cmdlist = {
        'help' : cmd_help,
        'list' : cmd_list,
        'time' : cmd_time,
        'set'  : cmd_set,
        'remove': cmd_remove
    }

    async def command_dispatch(self, message):
        '''Interprets incoming commands'''
        cmdBase = message.content.split(' ', 1)[0].lower()
        if cmdBase.startswith('tz.'):
            cmdBase = cmdBase[3:]
        else:
            return
        try:
            await self.cmdlist[cmdBase](self, message)
            tsPrint('Command invoked', '{0}/{1}: tz.{2}'.format(message.guild, message.author, cmdBase))
        except KeyError:
            pass
    
    async def on_message(self, message):
        # ignore bots (should therefore also ignore self)
        if message.author.bot: return
        # act on DMs
        if isinstance(message.channel, discord.DMChannel):
            tsPrint('Incoming DM', '{0}: {1}'.format(message.author, message.content.replace('\n', '\\n')))
            await message.channel.send('''I don't work over DM. Only in servers.''')
            # to do: small cache to not flood users who can't take a hint
            return
        self.udb.update_activity(message.guild.id, message.author.id)
        await self.command_dispatch(message)

    # ----------------

    async def periodic_report(self):
        '''
        Provides a periodic update in console of how many guilds we're on.
        Reports guild count to Discord Bots. Please don't make use of this unless you're the original author.
        '''
        try:
            authtoken = settings.DBotsApiKey
        except AttributeError:
            authtoken = ''

        await self.wait_until_ready()
        while not self.is_closed():
            guildcount = len(self.guilds)
            tsPrint("Report", "Currently in {0} guild(s).".format(guildcount))
            async with aiohttp.ClientSession() as session:
                if authtoken != '':
                    rurl = "https://bots.discord.pw/api/bots/{}/stats".format(self.user.id)
                    rdata = { "server_count": guildcount }
                    rhead = { "Content-Type": "application/json", "Authorization": authtoken }
                    try:
                        await session.post(rurl, json=rdata, headers=rhead)
                        tsPrint("Report", "Reported count to Discord Bots.")
                    except aiohttp.ClientError as e:
                        tsPrint("Report", "Discord Bots API report failed: {}".format(e))
            await asyncio.sleep(21600) # Repeat once every six hours