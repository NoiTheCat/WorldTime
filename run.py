#!/usr/bin/env python3

import asyncpg

from bot import WorldTime
import config
from source.utils import common
from source.utils.userdb import DatabaseUtil

common.log_print("World Time", f"World Time v {config.bot_version}")

bot = WorldTime()
bot.db = bot.loop.run_until_complete(asyncpg.create_pool(config.pg_uri))
bot.userdb = DatabaseUtil(bot.db)

for extension in common.EXTENSIONS:
    try:
        bot.load_extension(extension)
        print(f'[EXTENSION] {extension} was loaded successfully!')
    except Exception as e:
        print(f'[WARNING] Could not load extension {extension}: {e}')

bot.run(config.bot_token)
