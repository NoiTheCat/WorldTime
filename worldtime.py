#!/usr/bin/env python3

# World Time, a Discord bot. Displays user time zones.
# - https://github.com/NoiTheCat/WorldTime
# - https://discord.bots.gg/bots/447266583459528715

# Dependencies (install via pip or other means):
# pytz, psycopg2-binary, discord.py

from discord import Intents
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

    # todo: sharding options handled here: pass shard_id and shard_count parameters
    subscribedIntents = Intents.none()
    subscribedIntents.guilds = True
    subscribedIntents.members = True
    subscribedIntents.guild_messages = True
    client = WorldTime(
        max_messages=None,
        intents = subscribedIntents
    )
    
    client.run(settings.BotToken)
