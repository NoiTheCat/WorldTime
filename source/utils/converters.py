import typing

import discord
from discord.ext import commands

from source.utils import common

class TZConverter(commands.Converter):
    """A custom converter that attempts to convert the given argument
    into a pytz timezone."""

    async def convert(self, ctx, argument) -> typing.Optional[str]:
        try:
            return common.TIMEZONE_MAPPING[argument]
        except KeyError:
            pass
        raise commands.BadArgument(
    f"\U0000274c {argument} is not a valid time zone name.")


class BetterUserConverter(commands.Converter):
    """A custom converter that attempts to resolve the given argument
    to a Member or User. If both of those fails, it raises BadArgument."""

    async def convert(self, ctx, argument) -> typing.Optional[typing.Union[discord.Member, discord.User]]:
        try:
            user = await commands.MemberConverter().convert(ctx, argument)
            return user
        except commands.BadArgument:
            pass

        try:
            user = await commands.UserConverter().convert(ctx, argument)
            return user
        except commands.BadArgument:
            pass

        raise commands.BadArgument(
    f"I couldn't convert {argument} to a user or member.")