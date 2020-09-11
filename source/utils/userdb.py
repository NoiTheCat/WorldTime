import typing

import asyncpg

from source.utils.common import tz_format

class DatabaseUtil:
    """A simple database utility class for making interactions with our
    database slightly more sane."""

    def __init__(self, pool: asyncpg.pool.Pool):
        """Sets up the PostgreSQL connection to be used by this instance."""
        self.pool = pool

    async def update_activity(self, guild_id, user_id):
        """If a user exists in the database, updates their last activity timestamp."""

        query = """UPDATE userdata SET last_active = now()
                   WHERE guild_id = $1 AND user_id = $2"""

        await self.pool.execute(query, guild_id, user_id)

    async def delete_user(self, guild_id, user_id):
        """Deletes an existing user from the database."""

        query = """DELETE FROM userdata
                   WHERE guild_id = $1 AND user_id = $2"""

        await self.pool.execute(query, guild_id, user_id)

    async def update_user(self, guild_id, user_id, zone):
        """Insert or update user in the database.
        Does not do any sanitizing of incoming values, as only a small set of
        values are allowed anyway. This is enforced by the caller."""

        await self.delete_user(guild_id, user_id)

        query = """INSERT INTO userdata (guild_id, user_id, zone)
                   VALUES ($1, $2, $3)
                   ON CONFLICT (guild_id, user_id)
                   DO UPDATE SET zone = EXCLUDED.zone"""

        await self.pool.execute(query, guild_id, user_id, zone)

    async def get_user(self, guild_id, user_id):
        """Retrieves the time zone name of a single user."""

        query = """SELECT zone FROM userdata
                   WHERE guild_id = $1 and user_id = $2"""

        result = await self.pool.fetchrow(query, guild_id, user_id)

        if result is None:
            return None

        return result.get('zone')  # juuust in case

    async def get_users(self, guild_id) -> typing.Optional[dict]:
        """Retrieves all user time zones for all recently active members,
        or None if none are found.
        Users not present are not filtered here. Must be handled by the caller.

        Returns a dictionary of lists - key is formatted zone, value is list of users represented.
        For example: {'Africa/Abidjan': [123456, 987654], 'Europe/Warsaw': 567892}"""

        query = """
        SELECT zone, user_id
            FROM userdata
        WHERE
            last_active >= now() - INTERVAL '30 DAYS' -- only users active in the last 30 days
            AND guild_id = $1
            AND zone in (SELECT zone from (
                SELECT zone, count(*) as ct
                FROM userdata
                WHERE
                    guild_id = $1
                    AND last_active >= now() - INTERVAL '30 DAYS'
                GROUP BY zone
                LIMIT 20
            ) as pop_zones)
            ORDER BY RANDOM() -- Randomize display order (expected by consumer)"""

        results = await self.pool.fetch(query, guild_id)

        if not results:
            return None

        final = {}

        for row in results:
            formatted = tz_format(row['zone'])
            final[formatted] = final.get(formatted, [])
            final[formatted].append(row['user_id'])

        return final

    async def get_unique_tz_count(self) -> int:
        """Gets the number of unique time zones in the database."""

        results = await self.pool.fetch('SELECT COUNT(DISTINCT zone) FROM userdata')
        return results[0]['count']
