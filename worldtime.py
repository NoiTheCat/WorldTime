#!/usr/bin/env python3

# World Time, a Discord bot. Displays user time zones.
# - https://github.com/NoiTheCat/WorldTime
# - https://bots.discord.pw/bots/447266583459528715

# Dependencies (install via pip or other means):
# pytz, psycopg2, discord.py
# How to install the latter: pip install -U git+https://github.com/Rapptz/discord.py

from discord import Game
from client import WorldTime
import settings
import common

if __name__ == '__main__':
    common.logPrint("World Time", "World Time v" + common.BotVersion)
    
    try:
        # Raising AttributeError here to cover either: variable doesn't exist, or variable is empty
        if settings.BotToken == '': raise AttributeError() 
    except AttributeError:
        print("Bot token not set. Will not continue.")
        exit()

    # Note: Cannot disable guild_subscriptions - disables user cache when used in tandem w/ fetch_offline_members
    # todo: sharding options handled here: pass shard_id and shard_count parameters
    client = WorldTime(
        fetch_offline_members=False,
        # guild_subscriptions=False,
        max_messages=None,
    )
    
    client.run(settings.BotToken)
