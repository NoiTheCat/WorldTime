# Common items used throughout the project

import pytz
from datetime import datetime

# Bot's current version (as a string), for use in the help command
BotVersion = "1.3.4"

# For case-insensitive time zone lookup, map lowercase tzdata entries with
# entires with proper case. pytz is case sensitive.
tzlcmap = {x.lower():x for x in pytz.common_timezones}

def logPrint(label, line):
    """
    Print with timestamp in a way that resembles some of my other projects
    """
    resultstr = datetime.utcnow().strftime('%Y-%m-%d %H:%m:%S') + ' [' + label + '] ' + line
    print(resultstr)

def tzPrint(zone : str):
    """
    Returns a string displaying the current time in the given time zone.
    String begins with four numbers for sorting purposes and must be trimmed before output.
    """
    now_time = datetime.now(pytz.timezone(zone))
    return "{:s}● {:s}".format(now_time.strftime("%m%d"), now_time.strftime("%d-%b %H:%M %Z (UTC%z)"))