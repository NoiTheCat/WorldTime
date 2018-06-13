#!/usr/bin/env python3

# World Time, a Discord bot. Displays user time zones.
# - https://github.com/Noikoio/WorldTime
# - https://bots.discord.pw/bots/447266583459528715

# Using discord.py rewrite. To install:
# pip install -U git+https://github.com/Rapptz/discord.py@rewrite

# And yes, this code sucks. I don't know Python all too well.

# --------------------------------
# Required:
bot_token = ''
# --------------------------------

from discord import Game
from client import WorldTime

if __name__ == '__main__':
    try:
        if bot_token == '': raise NameError() # <- dumb
    except NameError:
        print("Bot token not set. Backing out.")
        exit()

    client = WorldTime(activity=Game('tz.help'))
    client.run(bot_token)
