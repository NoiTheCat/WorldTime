#!/usr/bin/env python3

# World Time, a Discord bot. Displays user time zones.
# - https://github.com/Noikoio/WorldTime
# - https://bots.discord.pw/bots/447266583459528715

# Using discord.py rewrite. To install:
# pip install -U git+https://github.com/Rapptz/discord.py@rewrite

# And yes, this code sucks. I don't know Python all too well.

from discord import Game
from wtclient import WorldTime
import settings

if __name__ == '__main__':
    try:
        if settings.BotToken == '': raise AttributeError() # Cover both scenarios: variable doesn't exist, or variable is empty
    except AttributeError:
        print("Bot token not set. Will not continue.")
        exit()

    client = WorldTime(activity=Game('tz.help'))
    client.run(settings.BotToken)
