# Common items used throughout the project

import pytz
from datetime import datetime

# Bot's current version (as a string), for use in the help command
BotVersion = "1.2.1"

# For case-insensitive time zone lookup, map lowercase tzdata entries with
# entires with proper case. pytz is case sensitive.
tzlcmap = {x.lower():x for x in pytz.common_timezones}

def logPrint(label, line):
    """
    Print with timestamp in a way that resembles some of my other projects
    """
    resultstr = datetime.utcnow().strftime('%Y-%m-%d %H:%m:%S') + ' [' + label + '] ' + line
    print(resultstr)