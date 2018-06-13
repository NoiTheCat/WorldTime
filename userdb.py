# Abstracts away details of the SQLite database that stores user information.

import sqlite3

class UserDatabase:
    def __init__(self, dbname):
        '''
        Sets up the SQLite session for the user database.
        '''
        self.db = sqlite3.connect(dbname)
        cur = self.db.cursor()
        cur.execute('''CREATE TABLE IF NOT EXISTS users(
            guild TEXT, user TEXT, zone TEXT, lastactive INTEGER,
            PRIMARY KEY (guild, user)
            )''')
        self.db.commit()
        cur.close()

    def update_activity(self, serverid : str, authorid : str):
        '''
        If a user exists in the database, updates their last activity timestamp.
        '''
        c = self.db.cursor()
        c.execute('''
            UPDATE users SET lastactive = strftime('%s', 'now')
            WHERE guild = '{0}' AND user = '{1}'
        '''.format(serverid, authorid))
        self.db.commit()
        c.close()

    def delete_user(self, serverid : str, authorid : str):
        '''
        Deletes existing user from the database.
        '''
        c = self.db.cursor()
        c.execute('''
            DELETE FROM users
            WHERE guild = '{0}' AND user = '{1}'
        '''.format(serverid, authorid))
        self.db.commit()
        c.close()

    def update_user(self, serverid : str, authorid : str, zone : str):
        '''
        Insert or update user in the database.
        Does not do any sanitizing of incoming values, as only a small set of
        values are allowed anyway. This is enforced by the caller.
        '''
        self.delete_user(serverid, authorid)
        c = self.db.cursor()
        c.execute('''
            INSERT INTO users VALUES
            ('{0}', '{1}', '{2}', strftime('%s', 'now'))
        '''.format(serverid, authorid, zone))
        self.db.commit()
        c.close()

    def get_list(self, serverid, userid=None):
        '''
        Retrieves a list of recent time zones based on
        recent activity per user. For use in the list command.
        '''
        c = self.db.cursor()
        if userid is None:
            c.execute('''
            SELECT zone, count(*) as ct FROM users
            WHERE guild = '{0}'
            AND lastactive >= strftime('%s','now') - (72 * 60 * 60) -- only users active in the last 72 hrs
            GROUP BY zone -- separate by popularity
            ORDER BY ct DESC LIMIT 10 -- top 10 zones are given
            '''.format(serverid))
        else:
            c.execute('''
            SELECT zone, '0' as ct FROM users
            WHERE guild = '{0}' AND user = '{1}'
            '''.format(serverid, userid))
            
        results = c.fetchall()
        c.close()
        return [i[0] for i in results]