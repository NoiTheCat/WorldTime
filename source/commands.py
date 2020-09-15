from datetime import datetime
import typing
import traceback
import textwrap
import io
from contextlib import redirect_stdout

import discord
from discord.ext import commands, menus
import pytz

from source.utils import converters
from source.utils.custom_help import CustomHelpCommand
from source.utils.common import tz_format


class ListSource(menus.ListPageSource):
    """A simple menu class for paginating lists."""

    def __init__(self, data):
        super().__init__(data, per_page=8)
        self.data = data

    async def format_page(self, menu, entry) -> discord.Embed:
        """An abstract method that formats each entry and returns
        them in an embed."""

        embed = discord.Embed(
            colour=menu.ctx.bot.colour,
            title=f'Registered timezones in {menu.ctx.guild.name}',
            description=entry[0] if len(self.data) == 1 else '\n'.join(entry)
            )

        embed.set_footer(
            text=f'Page {menu.current_page + 1}/{menu._source.get_max_pages()}')

        return embed


class WtCommands(commands.Cog):
    """The cog containing all of the bot's commands."""

    def __init__(self, bot):
        self.bot = bot
        self._original_help_command = bot.help_command
        bot.help_command = CustomHelpCommand()
        bot.help_command.cog = self

        self._last_result = None
        self.userdb = bot.userdb

    def cog_unload(self):
        self.bot.help_command = CustomHelpCommand()

    def cleanup_code(self, content):
        """Turns a codeblock into code that the bot can compile and execute."""

        if content.startswith('```') and content.endswith('```'):
            return '\n'.join(content.split('\n')[1:-1])
        return content.strip('` \n')

    def format_user(self, member: discord.Member) -> str:
        """Given a member, returns a formatted string showing their username and nickname
        prepared for result output."""

        full_name = discord.utils.escape_markdown(str(member))

        if member.nick is None:
            return f'**{full_name}**'

        nickname = discord.utils.escape_markdown(member.nick)
        return f'**{nickname}** ({full_name})'

    def format_users(self, users: typing.List[discord.Member]) -> str:
        """Given a list of members, pretty-formats each one and returns the list joined by `, `.
        If the list of users is longer than 10, it is sliced to that length and the list is
        appended with `and more...`"""

        if len(users) > 10:
            users = users[:-10]
            modified = [*map(self.format_user, users)]
            modified.append('and more...')
        else:
            modified = [*map(self.format_user, users)]

        return ', '.join(modified)

    async def _list_guild(self, ctx):
        """The helper function behind the command `tz list`."""

        userdict = await self.userdb.get_users(ctx.guild.id)

        if userdict is None:
            return await ctx.send(
                '\U0000274c No users with registered time zones have been active in the last 30 days.')

        iterating_dict = sorted(userdict.items())  # this sorts the entries by timezone
        tzs = []

        for k, v in iterating_dict:
            members = []

            for id_ in v:
                try:
                    member = await commands.MemberConverter().convert(ctx, str(id_))
                except commands.BadArgument:
                    continue
                else:
                    members.append(member)

            tzs.append(f"{k[4:]}: {self.format_users(members)}")

        pages = menus.MenuPages(ListSource(tzs), delete_message_after=True)
        await pages.start(ctx)

    async def _show(self, ctx, user):
        """The helper function behind the command `tz show`."""

        if user is None:
            user = ctx.author

        result = await self.userdb.get_user(ctx.guild.id, user.id)

        if result is None:
            if user == ctx.author:
                return await ctx.send(
                    f'\U0000274c You do not have a time zone. Set it with `{ctx.prefix}set`.')
                    # i use ctx.prefix here in case you want to add support for custom prefixes in the future

            return await ctx.send('\U0000274c The given user does not have a time zone set.')

        embed = discord.Embed(
            colour=ctx.bot.colour,
            description=f'{tz_format(result)[4:]}: {self.format_user(user)}')

        await ctx.send(embed=embed)

    @commands.command()
    async def ping(self, ctx):
        """Get the bot's Discord WebSocket latency."""
        await ctx.send(f'\U0001f3d3 Pong! {round(self.bot.latency * 1000, 1)} ms.')

    @commands.command(hidden=True)
    async def hello(self, ctx):
        """Displays the bot's intro message."""

        await ctx.send(
            f"Hello, I'm {self.bot.user.name}, a bot dedicated to keeping track of timezones on "
            f'your server. This instance is owned by {str(self.bot.get_user(self.bot.owner_id))}.')

    @commands.command()
    @commands.cooldown(1, 10, commands.BucketType.guild)
    async def invite(self, ctx):
        """Get an invite link to invite the bot to your server."""
        await ctx.send(f'<{self.bot.invite_url}>')

    @commands.group(invoke_without_command=True, cooldown_after_parsing=True, aliases=['tz'])
    async def timezone(self, ctx):
        """A base command for interacting with the bot's main feature, timezones."""
        await ctx.send_help(ctx.command)

    @timezone.command(name='set')
    @commands.cooldown(1, 5, commands.BucketType.user)
    async def tz_set(self, ctx, timezone: converters.TZConverter):
        """Registers or updates **your** timezone with the bot."""

        await self.userdb.update_user(ctx.guild.id, ctx.author.id, timezone)
        await ctx.send(f'\U00002705 Your timezone has been set to {timezone}.')

    @timezone.command(name='setfor')
    @commands.cooldown(1, 10, commands.BucketType.user)
    @commands.has_permissions(manage_guild=True)
    async def tz_setfor(self, ctx, target: discord.Member, timezone: converters.TZConverter):
        """Registers or updates the timezone of someone else in the server.
        This can only be used by members with the `Manage Server` permission."""

        await self.userdb.update_user(ctx.guild.id, target.id, timezone)
        await ctx.send(f'\U00002705 Set timezone for **{str(target)}** to {timezone}.')

    @timezone.command(name='remove', aliases=['wipe'])
    @commands.cooldown(1, 5, commands.BucketType.user)
    async def tz_remove(self, ctx):
        """Removes your data associated with this server from the bot. This cannot be undone."""

        await self.userdb.delete_user(ctx.guild.id, ctx.author.id)
        await ctx.send('\U00002705 Your timezone has been removed.')

    @timezone.command(name='removefor')
    @commands.cooldown(1, 10, commands.BucketType.user)
    @commands.has_permissions(manage_guild=True)
    async def tz_removefor(self, ctx, *, target: discord.Member):
        """Removes someone else's timezone data from this server from the bot. This cannot be undone.
        This can only be used by members with the `Manage Server` permission."""

        await self.userdb.delete_user(ctx.guild.id, target.id)
        await ctx.send(f'\U00002705 Removed zone information for **{str(target)}**.')

    @timezone.command(name='show')
    @commands.cooldown(1, 10, commands.BucketType.user)
    async def tz_show(self, ctx, *, user: discord.Member = None):
        """Either shows your or someone else's timezone."""
        await self._show(ctx, user)

    @timezone.command(name='list')
    @commands.cooldown(1, 10, commands.BucketType.guild)
    async def tz_list(self, ctx):
        """Shows a list of timezones registered in the guild."""
        await self._list_guild(ctx)

    @commands.command(name='eval')
    @commands.is_owner()
    async def _eval(self, ctx, *, body: str):
        """Evaluates Python code."""

        env = {
            'bot': self.bot,
            'ctx': ctx,
            'channel': ctx.channel,
            'author': ctx.author,
            'command': ctx.command,
            'guild': ctx.guild,
            'message': ctx.message,
            '_': self._last_result
        }

        env.update(globals())

        body = self.cleanup_code(body)
        stdout = io.StringIO()

        to_compile = f'async def func():\n{textwrap.indent(body, "  ")}'

        try:
            exec(to_compile, env)
        except Exception as e:
            return await ctx.send(f'```py\n{e.__class__.__name__}: {e}\n```')

        func = env['func']

        try:
            with redirect_stdout(stdout):
                ret = await func()
        except Exception as e:
            value = stdout.getvalue()
            await ctx.send(f'```py\n{value}{traceback.format_exc()}\n```')
        else:
            value = stdout.getvalue()
            try:
                await ctx.message.add_reaction('\U00002705')
            except Exception:
                pass

            if ret is None:
                if value:
                    await ctx.send(f'```py\n{value}\n```')
            else:
                self._last_result = ret
                await ctx.send(f'```py\n{value}{ret}\n```')

def setup(bot):
    bot.add_cog(WtCommands(bot))
