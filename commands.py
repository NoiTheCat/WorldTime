# Command handlers
# Incoming commands are fully handled by functions defined here.

from common import BotVersion
from textwrap import dedent
import discord
import pytz
from datetime import datetime
import re

from userdb import UserDatabase
from common import tzlcmap, logPrint

class WtCommands:
    def __init__(self, userdb: UserDatabase, client: discord.Client):
        self.userdb = userdb
        self.dclient = client
        self.commandlist = {
            'help' : self.cmd_help,
            'list' : self.cmd_list,
            'time' : self.cmd_time,
            'set'  : self.cmd_set,
            'remove': self.cmd_remove,
            'setfor': self.cmd_setFor,
            'removefor': self.cmd_removeFor
        }
    
    async def dispatch(self, cmdBase: str, message: discord.Message):
        try:
            command = self.commandlist[cmdBase]
        except KeyError:
            return
        logPrint('Command invoked', '{0}/{1}: tz.{2}'.format(message.guild, message.author, cmdBase))
        await command(message.guild, message.channel, message.author, message.content)

    # ------
    # Helper methods

    def _tzPrint(self, zone : str):
        """
        Returns a string displaying the current time in the given time zone.
        Resulting string should be placed in a code block.
        """
        padding = ''
        now_time = datetime.now(pytz.timezone(zone))
        if len(now_time.strftime("%Z")) != 4: padding = ' '
        return "{:s}{:s} | {:s}".format(now_time.strftime("%H:%M %d-%b %Z%z"), padding, zone)

    def _userResolve(self, guild: discord.Guild, userIds: list):
        """
        Given a list with user IDs, returns a string, the second half of a
        list entry, describing the users for which a zone is represented by.
        """
        if len(userIds) == 0:
            return "    → This text should never appear."
        
        # Try given entries. For each entry tried, attempt to get their nickname
        # or username. Failed attempts are anonymized instead of discarded.
        namesProcessed = 0
        namesSkipped = 0
        processedNames = []
        while namesProcessed < 4 and len(userIds) > 0:
            namesProcessed += 1
            uid = userIds.pop()
            mem = guild.get_member(int(uid))
            if mem is not None:
                processedNames.append(mem.display_name)
            else:
                namesSkipped += 1
        leftovers = namesSkipped + len(userIds)
        if len(processedNames) == 0:
            return "    → {0} user{1}.".format(leftovers, "s" if leftovers != 1 else "")
        result = "    → "
        while len(processedNames) > 0:
            result += processedNames.pop() + ", "
        if leftovers != 0:
            result += "{0} other{1}.".format(leftovers, "s" if leftovers != 1 else "")
        else:
            result = result[:-2] + "."
        return result

    def _isUserAdmin(self, member: discord.Member):
        """
        Checks if the given user can be considered a guild admin ('Manage Server' is set).
        """
        # Can fit in a BirthdayBot-like bot moderator role in here later, if desired.
        p = member.guild_permissions
        return p.administrator or p.manage_guild

    def _resolveUserParam(self, guild: discord.Guild, input: str):
        """
        Given user input, attempts to find the corresponding Member.
        Currently only accepts pings and explicit user IDs.
        """
        UserMention = re.compile(r"<@\!?(\d+)>")
        match = UserMention.match(input)
        if match is not None:
            idcheck = match.group(1)
        else:
            idcheck = input
        try:
            idcheck = int(idcheck)
        except ValueError:
            return None
        return guild.get_member(idcheck)

    # ------
    # Individual command handlers
    # All command functions are expected to have this signature:
    # def cmd_NAME(self, guild: discord.Guild, channel: discord.TextChannel, author: discord.User, msgcontent: str)

    async def cmd_help(self, guild: discord.Guild, channel: discord.TextChannel, author: discord.User, msgcontent: str):
        # Be a little fancy.
        tzcount = self.userdb.get_unique_tz_count()

        em = discord.Embed(
            color=14742263,
            title='Help & About',
            description=dedent('''
                World Time v{0}
                Serving {1} communities across {2} time zones.
            '''.format(BotVersion, len(self.dclient.guilds), tzcount))
        )
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
        em.add_field(name='Admin commands', value=dedent('''
            `tz.setFor [user] [zone]` - Sets the time zone for another user.
            `tz.removeFor [user]` - Removes another user's information.
        '''), inline=False)
        em.add_field(name='Zones', value=dedent('''
            This bot uses zone names from the tz database. Most common zones are supported. For a list of entries, see the "TZ database name" column under https://en.wikipedia.org/wiki/List_of_tz_database_time_zones.
        '''), inline=False)
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
            resultstr = '```\n' + self._tzPrint(zoneinput) + '\n```'
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

    async def cmd_setFor(self, guild: discord.Guild, channel: discord.TextChannel, author: discord.User, msgcontent: str):
        if not self._isUserAdmin(author):
            # Silently ignore
            return

        # parameters: command, target, zone
        wspl = msgcontent.split(' ', 2)
        
        if len(wspl) == 1:
            await channel.send(":x: You must specify a user to set the time zone for.")
            return
        if len(wspl) == 2:
            await channel.send(":x: You must specify a time zone to apply to the user.")
            return
        
        # Determine user from second parameter
        targetuser = self._resolveUserParam(guild, wspl[1])
        if targetuser is None:
            await channel.send(":x: Unable to find the target user.")
            return
        
        # Check the third parameter
        try:
            zoneinput = tzlcmap[wspl[2].lower()]
        except KeyError:
            await channel.send(':x: Not a valid zone name.')
            return

        # Do the thing
        self.userdb.update_user(guild.id, targetuser.id, zoneinput)
        await channel.send(':white_check_mark: Set zone for **' + targetuser.name + '#' + targetuser.discriminator + '**.')

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

    async def cmd_removeFor(self, guild: discord.Guild, channel: discord.TextChannel, author: discord.User, msgcontent: str):
        if not self._isUserAdmin(author):
            # Silently ignore
            return

        # Parameters: command, target
        wspl = msgcontent.split(' ', 1)
        
        if len(wspl) == 1:
            await channel.send(":x: You must specify a user for whom to remove time zone data.")
            return
        targetuser = self._resolveUserParam(guild, wspl[1])
        if targetuser is None:
            await channel.send(":x: Unable to find the target user.")
            return
        
        self.userdb.delete_user(guild.id, targetuser.id)
        await channel.send(':white_check_mark: Removed zone information for **' + targetuser.name + '#' + targetuser.discriminator + '**.')

    # ------
    # Supplemental command functions

    async def _list_noparam(self, guild: discord.Guild, channel: discord.TextChannel):
        # To do: improve and merge into noparam2
        clist = self.userdb.get_list(guild.id)
        if len(clist) == 0:
            await channel.send(':x: No users with registered time zones have been active in the last 30 days.')
            return
        resultarr = []
        for i in clist:
            resultarr.append(self._tzPrint(i))
        resultarr.sort()
        resultstr = '```\n'
        for i in resultarr:
            resultstr += i + '\n'
        resultstr += '```'
        await channel.send(resultstr)

    async def _list_noparam2(self, guild: discord.Guild, channel: discord.TextChannel):
        rawlist = self.userdb.get_list2(guild.id)
        if len(rawlist) == 0:
            await channel.send(':x: No users with registered time zones have been active in the last 30 days.')
            return
        if guild.large:
            # Get full user data here if not available (used by _userResolve)
            await self.dclient.request_offline_members(guild)
        resultData = []
        for key, value in rawlist.items():
            resultData.append(self._tzPrint(key) + '\n' + self._userResolve(guild, value))
        resultData.sort()
        resultFinal = '```\n'
        for i in resultData:
            resultFinal += i + '\n'
        resultFinal += '```'
        await channel.send(resultFinal)

    async def _list_userparam(self, guild: discord.Guild, channel: discord.TextChannel, author: discord.User, param):
        # wishlist: search based on username/nickname
        param = str(param)
        usersearch = self._resolve_user(guild, param)
        if usersearch is None:
            await channel.send(':x: Cannot find the specified user.')
            return

        res = self.userdb.get_list(guild.id, usersearch.id)
        if len(res) == 0:
            ownsearch = author.id == param
            if ownsearch: await channel.send(':x: You do not have a time zone. Set it with `tz.set`.')
            else: await channel.send(':x: The given user does not have a time zone set.')
            return
        resultstr = '```\n' + self._tzPrint(res[0]) + '\n```'
        await channel.send(resultstr)

    def _resolve_user(self, guild: discord.Guild, inputstr: str):
        """
        Takes a string input and attempts to find the corresponding user.
        """
        idsearch = None
        try:
            idsearch = int(inputstr)
        except ValueError:
            pass
        if inputstr.startswith('<@!') and inputstr.endswith('>'):
            idsearch = inputstr[3:][:-1]
        if inputstr.startswith('<@') and inputstr.endswith('>'):
            idsearch = inputstr[2:][:-1]
        if idsearch is not None:
            return guild.get_member(idsearch)

        # get_member_named is case-sensitive. we do it ourselves. username only.
        for member in guild.members:
            # we'll use the discriminator and do a username lookup if it exists
            if len(inputstr) > 5 and inputstr[-5] == '#':
                discstr = inputstr[-4:]
                userstr = inputstr[:-5]
                if discstr.isdigit():
                    if member.discriminator == discstr and userstr.lower() == member.name.lower():
                        return member
            #nickname search
            if member.nick is not None:
                if member.nick.lower() == inputstr.lower():
                    return member
            #username search
            if member.name.lower() == inputstr.lower():
                return member

        return None