# World Time

A social time zone reference tool! Displays the current time for all your active members.

* Info: https://discord.bots.gg/bots/447266583459528715
* Invite: https://discordapp.com/oauth2/authorize?client_id=447266583459528715&scope=bot&permissions=0

For more information, see the `DiscordBots.md` file.

## Setup
1. Install the necessary dependencies from `requirements.txt` (`pip install requirements.txt` or any variation).
2. Install PostgreSQL on your system, and then run the following schema to create the userdata table:
```sql
CREATE TABLE IF NOT EXISTS userdata (
guild_id BIGINT,
user_id BIGINT,
zone TEXT NOT NULL,
last_active TIMESTAMPTZ NOT NULL DEFAULT now(),
PRIMARY KEY (guild_id, user_id)
)
```
3. Replace the respective values from `config_example.py` to a file named `config.py`.
4. Run `run.py`.

## Links
- [Repository](https://github.com/NoiTheCat/WorldTime)
- [Discord Bots](https://bots.discord.pw/bots/447266583459528715)
