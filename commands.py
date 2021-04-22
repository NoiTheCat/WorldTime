# Command handlers
# Incoming commands are fully handled by functions defined here.

from common import BotVersion, tzPrint
from textwrap import dedent
import discord
import pytz
from datetime import datetime
import re
import collections

from userdb import UserDatabase
from common import tzlcmap, logPrint

class WtCommands:
    def __init__(self, userdb: UserDatabase, client: discord.Client):
        self.userdb = userdb
        self.dclient = client
        self.commandlist = {
            'help' : self.cmd_help,
            'list' : self.cmd_list,
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
        logPrint('Command invoked', '{0}/{1}: {2}'.format(message.guild, message.author, message.content))
        await command(message.guild, message.channel, message.author, message.content)

    # ------
    # Helper methods
    
    def _userFormat(self, member: discord.Member):
        """
        Given a member, returns a formatted string showing their username and nickname
        prepared for result output.
        """
        username = self._userFormatEscapeFormattingCharacters(member.name)
        if member.nick is not None:
            nickname = self._userFormatEscapeFormattingCharacters(member.nick)
            return "**{}** ({}#{})".format(nickname, username, member.discriminator)
        else:
            return "**{}#{}**".format(username, member.discriminator)

    def _userFormatEscapeFormattingCharacters(self, input: str):
        result = ''
        for char in input:
            if char == '\\' or char == '_' or char == '~' or char == '*':
                result += '\\'
            result += char
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
            await self._list_noparam(guild, channel)
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
        userlist = self.userdb.get_users(guild.id)
        if len(userlist) == 0:
            await channel.send(':x: No users with registered time zones have been active in the last 30 days.')
            return
        
        orderedList = collections.OrderedDict(sorted(userlist.items()))
        result = ''

        for k, v in orderedList.items():
            foundUsers = 0
            line = k[4:] + ": "
            for id in v:
                member = await self._resolve_member(guild, id)
                if member is None:
                    continue
                if foundUsers > 10:
                    line += "and others...  "
                foundUsers += 1
                line += self._userFormat(member) + ", "
            if foundUsers > 0: result += line[:-2] + "\n"
        
        em = discord.Embed(description=result.strip())
        await channel.send(embed=em)

    async def _list_userparam(self, guild: discord.Guild, channel: discord.TextChannel, author: discord.User, param):
        param = str(param)
        usersearch = await self._resolve_member(guild, param)
        if usersearch is None:
            await channel.send(':x: Cannot find the specified user.')
            return

        res = self.userdb.get_user(guild.id, usersearch.id)
        if res is None:
            ownsearch = author.id == param
            if ownsearch: await channel.send(':x: You do not have a time zone. Set it with `tz.set`.')
            else: await channel.send(':x: The given user does not have a time zone set.')
            return
        em = discord.Embed(description=tzPrint(res)[4:] + ": " + self._userFormat(usersearch))
        await channel.send(embed=em)

    async def _resolve_member(self, guild: discord.Guild, inputstr: str):
        """
        Takes a string input and attempts to find the corresponding member.
        """
        if not guild.chunked: await guild.chunk()
        idsearch = None
        try:
            idsearch = int(inputstr)
        except ValueError:
            if inputstr.startswith('<@!') and inputstr.endswith('>'):
                idsearch = int(inputstr[3:][:-1])
            elif inputstr.startswith('<@') and inputstr.endswith('>'):
                idsearch = int(inputstr[2:][:-1])

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