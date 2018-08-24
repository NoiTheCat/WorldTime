# Common items used throughout the project

import pytz
from datetime import datetime

# For case-insensitive time zone lookup, map lowercase tzdata entries with
# entires with proper case. pytz is case sensitive.
tzlcmap = {x.lower():x for x in pytz.common_timezones}

timefmt = "%H:%M %d-%b %Z%z"
def tzPrint(zone : str):
    """
    Returns a string displaying the current time in the given time zone.
    Resulting string should be placed in a code block.
    """
    padding = ''
    now_time = datetime.now(pytz.timezone(zone))
    if len(now_time.strftime("%Z")) != 4: padding = ' '
    return "{:s}{:s} | {:s}".format(now_time.strftime(timefmt), padding, zone)

def logPrint(label, line):
    """
    Print with timestamp in a way that resembles some of my other projects
    """
    resultstr = datetime.utcnow().strftime('%Y-%m-%d %H:%m:%S') + ' [' + label + '] ' + line
    print(resultstr)