# Common items used throughout the project

from datetime import datetime
import pytz

# For case-insensitive time zone lookup, map lowercase tzdata entries with
# entires with proper case. pytz is case sensitive.
TIMEZONE_MAPPING = {x.lower(): x for x in pytz.common_timezones}

def log_print(label, line):
    """
    Print with timestamp in a way that resembles some of my other projects
    """
    result = f"{datetime.utcnow().strftime('%Y-%m-%d %H:%m:%S')} [ {label} ] {line}"
    print(result)

def tz_format(zone: str) -> str:
    """
    Returns a string displaying the current time in the given time zone.
    String begins with four numbers for sorting purposes and must be trimmed before output.
    """
    now_time = datetime.now(pytz.timezone(zone))
    return "{:s}● {:s}".format(now_time.strftime("%m%d"), now_time.strftime("%d-%b %H:%M %Z (UTC%z)"))