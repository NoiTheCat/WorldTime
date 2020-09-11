#!/usr/bin/env python3

import asyncpg

from bot import WorldTime
from source.utils import common
from source.utils.userdb import DatabaseUtil

common.log_print("World Time", f"World Time v {config.bot_version}")

bot = WorldTime()
bot.db = bot.loop.run_until_complete(asyncpg.create_pool(config.pg_uri))
bot.userdb = DatabaseUtil(bot.db)

bot.run(config.bot_token)
