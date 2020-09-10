#!/usr/bin/env python3

# World Time, a Discord bot. Displays user time zones.
# - https://github.com/NoiTheCat/WorldTime
# - https://bots.discord.pw/bots/447266583459528715

# Dependencies (install via pip or other means):
# pytz, asyncpg discord.py

# Once you've installed PostgreSQL on your system, somehow
# run the following schema to create the userdata table:

# CREATE TABLE IF NOT EXISTS userdata (
# guild_id BIGINT,
# user_id BIGINT,
# zone TEXT NOT NULL,
# last_active TIMESTAMPTZ NOT NULL DEFAULT now(),
# PRIMARY KEY (guild_id, user_id)
# )

# replace the respective values in config_example.py and then run this
# file in whichever way you like and you should be good to go!

import asyncpg

from bot import WorldTime
from source.utils import common
from source.utils.userdv import DatabaseUtil

common.log_print("World Time", f"World Time v {config.bot_version}")

bot = WorldTime()
bot.db = bot.loop.run_until_complete(asyncpg.create_pool(config.pg_uri))
bot.userdb = DatabaseUtil(bot.db)

bot.run(config.bot_token)
