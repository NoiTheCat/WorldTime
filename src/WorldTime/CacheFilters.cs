using Microsoft.EntityFrameworkCore;
using WorldTime.Data;

namespace WorldTime;

static class CacheFilters
{
    /// <summary>
    /// Provides a filter that returns a list of all users present with bot configuration,
    /// excluding those already in the local cache.
    /// </summary>
    internal static UserCache<BotDatabaseContext>.AsyncCacheFetchFilter RegisteredNotInCache => async (cache, context, guildId) =>
    {
        IEnumerable<ulong> local;
        var existing = cache.GetGuild(guildId, true);
        if (existing == null) local = [];
        else local = existing.Select(e => e.Value.UserId);

        var remote = await context.UserEntries
            .Where(e => e.GuildId == guildId)
            .Select(e => e.UserId)
            .ToListAsync().ConfigureAwait(false);

        return [.. remote.Except(local)];
    };

    /// <summary>
    /// Provides a filter that returns a list of users registered with the bot,
    /// not already in the local cache, with information that's nearly at the
    /// auto-deletion threshold for <see cref="DataJanitor"/>.
    /// </summary>
    internal static UserCache<BotDatabaseContext>.AsyncCacheFetchFilter NearExpiryNotInCache => async (cache, context, guildId) =>
    {
        IEnumerable<ulong> local;
        var existing = cache.GetGuild(guildId, true);
        if (existing == null) local = [];
        else local = existing.Select(e => e.Value.UserId);

        var cutoff = SystemClock.Instance.GetCurrentInstant() - Duration.FromDays(DataJanitor.PreDeleteCheckThreshold);
        var remoteExpiring = await context.UserEntries
            .Where(e => e.GuildId == guildId)
            .Where(e => cutoff > e.LastSeen)
            .Select(e => e.UserId)
            .ToListAsync().ConfigureAwait(false);

        return [.. remoteExpiring.Except(local)];
    };
}
