#!/usr/bin/env python3

# World Time, a Discord bot. Displays user time zones.
# - https://github.com/Noikoio/WorldTime
# - https://bots.discord.pw/bots/447266583459528715

# -----------
# Required:
bot_token = ''
# Optional. Don't delete or modify in a self-hosted instance.
dbots_token = ''
# -----------

# Bad code ahead. I knew next to nothing about Python and I also made hasty desicions throughout.
# Just keep that in mind if you decide to pick apart this code.

# Not using discord.ext.commands extension, because see above.
# Namely, can't figure out how to use it while also having my own on_message.
# *And* I couldn't figure out how to customize its help message.

import discord
import asyncio

from datetime import datetime
import pytz

from UserDatabase import UserDatabase

def tsPrint(label, line):
    """
    Print with timestamp in a way that resembles some of my other projects
    """
    resultstr = datetime.utcnow().strftime('%Y-%m-%d %H:%m:%S') + ' [' + label + '] ' + line
    print(resultstr)

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

db = UserDatabase('users.db')

# ---
# Command things

def build_help_embed(emtitle, emdesc):
    em = discord.Embed(
        color=14742263,
        title=emtitle,
        description=emdesc)
    em.set_footer(text='World Time', icon_url=bot.user.avatar_url)
    
    return em

async def cmd_help(message : discord.Message):
    em = build_help_embed(
        'Help & About',
        'This bot aims to answer the age-old question, "What time is it for everyone here?"')
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
    await bot.send_message(message.channel, embed=em)

async def cmd_list(message : discord.Message):
    wspl = message.content.split(' ', 1)
    if len(wspl) == 1:
        await cmd_list_noparam(message)
    else:
        await cmd_list_userparam(message, wspl[1])
async def cmd_list_noparam(message : discord.Message):
    clist = db.get_list(message.channel.server.id)
    if len(clist) == 0:
        await bot.send_message(message.channel, ':x: No users with known zones have been active in the last 72 hours.')
        return
    resultarr = []
    for i in clist:
        resultarr.append(tzPrint(i))
    resultarr.sort()
    resultstr = '```\n'
    for i in resultarr:
        resultstr += i + '\n'
    resultstr += '```'
    await bot.send_message(message.channel, resultstr)
async def cmd_list_userparam(message : discord.Message, param):
    # wishlist: search based on username/nickname
    if param.startswith('<@!') and param.endswith('>'):
        param = param[3:][:-1]
    if param.startswith('<@') and param.endswith('>'):
        param = param[2:][:-1]
    if not param.isnumeric():
        # Didn't get an ID...
        await bot.send_message(message.channel, ':x: You must specify a user by ID or `@` mention.')
        return
    res = db.get_list(message.channel.server.id, param)
    if len(res) == 0:
        spaghetti = message.author.id == param
        if spaghetti: await bot.send_message(message.channel, ':x: You do not have a time zone. Set it with `tz.set`.')
        else: await bot.send_message(message.channel, ':x: The given user has not set a time zone. Ask to set it with `tz.set`.')
        return
    resultstr = '```\n' + tzPrint(res[0]) + '\n```'
    await bot.send_message(message.channel, resultstr)

async def cmd_time(message : discord.Message):
    wspl = message.content.split(' ', 1)
    if len(wspl) == 1:
        # No parameter. So, doing the same thing anyway...
        await cmd_list_userparam(message, message.author.id)
    else:
        try:
            zoneinput = tzlcmap[wspl[1].lower()]
        except KeyError:
            await bot.send_message(message.channel, ':x: Not a valid zone name.')
            return
        resultstr = '```\n' + tzPrint(zoneinput) + '\n```'
        await bot.send_message(message.channel, resultstr)

async def cmd_set(message : discord.Message):
    wspl = message.content.split(' ', 1)
    if len(wspl) == 1:
        # No parameter. But it's required
        await bot.send_message(message.channel, ':x: Zone parameter is required.')
        return
    try:
        zoneinput = tzlcmap[wspl[1].lower()]
    except KeyError:
        await bot.send_message(message.channel, ':x: Not a valid zone name.')
        return
    db.update_user(message.channel.server.id, message.author.id, zoneinput)
    await bot.send_message(message.channel, ':white_check_mark: Your zone has been set.')

async def cmd_remove(message : discord.Message):
    db.delete_user(message.channel.server.id, message.author.id)
    await bot.send_message(message.channel, ':white_check_mark: Your zone has been removed.')

cmdlist = {
    'help' : cmd_help,
    'list' : cmd_list,
    'time' : cmd_time,
    'set'  : cmd_set,
    'remove': cmd_remove
}

async def command_dispatch(message : discord.Message):
    '''Interprets incoming commands'''
    thecmd = message.content.split(' ', 1)[0].lower()
    if thecmd.startswith('tz.'):
        thecmd = thecmd[3:]
    else:
        return
    try:
        await cmdlist[thecmd](message)
        tsPrint('Command invoked', '{0}/{1}: tz.{2}'.format(message.channel.server, message.author, thecmd))
    except KeyError:
        pass

# ---
# Bot things

bot = discord.Client()

@bot.event
async def on_ready():
    tsPrint('Status', 'Connected as {0} ({1})'.format(bot.user.name, bot.user.id))
    await bot.change_presence(game=discord.Game(name='tz.help'))

@bot.event
async def on_message(message : discord.Message):
    # ignore bots (should therefore also ignore self)
    if message.author.bot: return
    # act on DMs
    if isinstance(message.channel, discord.PrivateChannel):
        tsPrint('Incoming DM', '{0}: {1}'.format(message.author, message.content.replace('\n', '\\n')))
        await bot.send_message(message.channel, '''I can't work over DMs...''')
        # to do: small cache to not flood users who can't take a hint
        return
    db.update_activity(message.server.id, message.author.id)
    await command_dispatch(message)

# ---
# This periodically sends the server count to Discord Bots.
# If you're not the original author of this bot, you have no reason at all to be using this.
if dbots_token != '':
    import aiohttp
    async def dbots_update():
        await bot.wait_until_ready()
        while not bot.is_closed:
            serverct = len(bot.servers)
            async with aiohttp.ClientSession() as session:
                rurl = "https://bots.discord.pw/api/bots/{}/stats".format(bot.user.id)
                rdata = { "server_count": serverct }
                rhead = { "Content-Type": "application/json", "Authorization": dbots_token }
                try:
                    await session.post(rurl, json=rdata, headers=rhead)
                    tsPrint("Report", "Reporting {0} guild(s) to Discord Bots.".format(serverct))
                except aiohttp.ClientError as e:
                    tsPrint("Report", "Discord Bots API report failed: {}".format(e))
            await asyncio.sleep(21600) # Update once every six hours

# ---

if __name__ == '__main__':
    tsPrint('Status', 'Hello. Using users.db for database.')
    if dbots_token != '':
        bot.loop.create_task(dbots_update())
    bot.run(bot_token)