#!/usr/bin/env python3

# Utility for carrying over data from an older SQLite
# database into a newer PostgreSQL one.

# Edit this value:
PgConnectionString = ''

import sqlite3
import psycopg2

sldb = sqlite3.connect('users.db')
slcur = sldb.cursor()
pgdb = psycopg2.connect(PgConnectionString)
pgcur = pgdb.cursor()

pgcur.execute("""
        CREATE TABLE IF NOT EXISTS userdata (
        guild_id BIGINT,
        user_id BIGINT,
        zone TEXT NOT NULL,
        last_active TIMESTAMPTZ NOT NULL DEFAULT now(),
        PRIMARY KEY (guild_id, user_id)
        )""")
pgdb.commit()
pgcur.execute("TRUNCATE TABLE userdata")
pgdb.commit()

slcur.execute('''
    SELECT guild, user, zone, lastactive
    FROM users
    ''')

for row in slcur:
    pgcur.execute('''
        INSERT INTO userdata (guild_id, user_id, zone, last_active)
        VALUES (%s, %s, %s, to_timestamp(%s) at time zone 'utc')
        ''', (int(row[0]), int(row[1]), row[2], int(row[3])))
    pgdb.commit()
    print(row)