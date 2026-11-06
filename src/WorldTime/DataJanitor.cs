using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoiPublicBot.BackgroundServices;
using Npgsql;
using WorldTime.Data;

namespace WorldTime;

// Keeps track of known existing users. Removes old unused data
sealed class DataJanitor : BackgroundService
{
    /*
    This rewrite is very similar to the DataJanitor in BirthdayBot, but cannot assume
    that user entries will always be made available automatically. Excluding this class,
    cache entries in this bot only occur only as a direct result of user action.

    To avoid needlessly overfilling the local cache, any cache fill requests will only
    be for users with data about to expire.
    */

    // Process about once every six hours
    private static readonly Duration ProcessInterval = Duration.FromHours(6);

    // Number of days without being seen before data is considered stale and up for deletion.
    public const int DeleteThreshold = 90;
    // Number of days without being seen before DataJanitor attempts to check up on the user.
    public const int PreDeleteCheckThreshold = DeleteThreshold - 2;

    // Start the first run about 10 minutes from initialization
    private Instant _lastRun = SystemClock.Instance.GetCurrentInstant() - (ProcessInterval - Duration.FromMinutes(10));

    public override async Task OnTick(int tickCount, CancellationToken token)
    {
        var sinceLast = SystemClock.Instance.GetCurrentInstant() - _lastRun;
        if (sinceLast < ProcessInterval)
        {
            Log.Verbose("Interval not yet reached ({TimeSinceLast}). Not proceeding.", sinceLast);
            return;
        }

        using var db = BotDatabaseContext.New();

        // As stated above, this is the point where user info is fetched for those about to expire.
        // This task has the potential to get stuck here for a while.
        var cache = Shard.LocalServices.GetRequiredService<UserCache<BotDatabaseContext>>();
        await cache.BackgroundRefreshWholeShardAsync(db, CacheFilters.NearExpiryNotInCache, token).ConfigureAwait(false);

        // For more complete logging, checking users before guilds
        await UpdateDeleteUsersAsync(db);
        await UpdateGuildsAsync(db);
        if (Shard.ShardId == 0) await DeleteGuildsAsync(db); // Shard 0 only: drop old guild records
        _lastRun = SystemClock.Instance.GetCurrentInstant();
    }

    private async Task UpdateGuildsAsync(BotDatabaseContext db)
    {
        var today = SystemClock.Instance.GetCurrentInstant().InUtc().Date;

        // Keeping track of guilds is easy; just see if we're joined or not.
        var present = Shard.DiscordClient.Guilds.Select(g => g.Id).ToHashSet();
        Log.Debug("Obtained {GuildCount} current guild IDs.", present.Count);
        var updateCt = await db.GuildSettings
            .Where(g => present.Contains(g.GuildId))
            .Where(g => today > g.LastSeen)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(c => c.LastSeen, today))
            .ConfigureAwait(false);
        if (updateCt != 0) Log.Information("Updated {UpdatedGuilds} guild records.", updateCt);
    }

    private async Task DeleteGuildsAsync(BotDatabaseContext db)
    {
        var today = SystemClock.Instance.GetCurrentInstant().InUtc().Date;
        var cutoff = today - Period.FromDays(DeleteThreshold);

        var deleteCt = await db.GuildSettings
            .Where(gc => cutoff > gc.LastSeen)
            .ExecuteDeleteAsync().ConfigureAwait(false);
        if (deleteCt != 0) Log.Information("Removed {DeletedGuilds} stale guild record(s).", deleteCt);
    }

    private async Task UpdateDeleteUsersAsync(BotDatabaseContext db)
    {
        var clone = Shard.LocalServices
            .GetRequiredService<UserCache<BotDatabaseContext>>()
            .GetAll(includeNullEntries: true);
        var flat = clone.SelectMany(c => c.Value, (outer, inner) =>
            new { GuildId = outer.Key, UserId = inner.Key, Data = inner.Value })
            .ToList();
        Log.Debug("Cache clone flattened to {FlatCount} entries.", flat.Count);

        var present = flat
            .Where(v => !v.Data.IsNull)
            .Select(v => (v.GuildId, v.UserId))
            .ToList();
        // Users in the absent set are those whose data was requested but that UserCache was unable to find.
        // In this situation, a user who may have an entry is known to not be present and is thus eligible for deletion check.
        var absent = flat
            .Where(v => v.Data.IsNull)
            .Select(v => (v.GuildId, v.UserId))
            .ToList();
        Log.Debug("Have {PresentCount} present, {AbsentCount} absent user(s).", present.Count, absent.Count);

        // Present users: update LastSeen
        var updateCt = await RawUpdateUsersAsync(db, present).ConfigureAwait(false);
        Log.Information("Updated {UpdatedUsers} user records.", updateCt);
        // Absent users: if it's been long enough, delete them
        var deleteCt = await RawDeleteUsersAsync(db, absent).ConfigureAwait(false);
        if (deleteCt != 0) Log.Information("Removed {DeletedUsers} stale user records.", deleteCt);
    }

    #region Manual SQL query setup
    // EF Core does not have a way to do bulk operations with composite primary keys.
    // This little section handles writing the appropriate SQL manually.

    // Caching these since they're referred to quite often
    private struct SqlNames
    {
        public string TblUserEntry { get; private init; }
        public string UColGuildId { get; private init; }
        public string UColUserId { get; private init; }
        public string UColLastSeen { get; private init; }

        static SqlNames? Instance;
        public static SqlNames Get(BotDatabaseContext db)
        {
            if (!Instance.HasValue)
            {
#nullable disable
                var user = db.Model.FindEntityType(typeof(UserEntry));
                Instance = new()
                {
                    TblUserEntry = $"\"{user.GetTableName()}\"",
                    UColGuildId = $"\"{user.FindProperty(nameof(UserEntry.GuildId)).GetColumnName()}\"",
                    UColUserId = $"\"{user.FindProperty(nameof(UserEntry.UserId)).GetColumnName()}\"",
                    UColLastSeen = $"\"{user.FindProperty(nameof(UserEntry.LastSeen)).GetColumnName()}\""
                };
#nullable restore
            }
            return Instance.Value;
        }

    }

    private async Task<int> RawUpdateUsersAsync(BotDatabaseContext db, IEnumerable<(ulong, ulong)> keys)
    {
        var n = SqlNames.Get(db);

        // Give pairs as arrays, turn them into a Postgres set, check against it when selecting
        var sql = $"""
        UPDATE {n.TblUserEntry} AS t
            SET {n.UColLastSeen} = CURRENT_DATE
        FROM unnest(@pkey1, @pkey2) AS k(gid, uid)
        WHERE
            t.{n.UColGuildId} = k.gid AND t.{n.UColUserId} = k.uid
            AND CURRENT_DATE > t.{n.UColLastSeen}
        """;
        return await db.Database.ExecuteSqlRawAsync(sql, Parameterize(keys)).ConfigureAwait(false);
    }

    private async Task<int> RawDeleteUsersAsync(BotDatabaseContext db, IEnumerable<(ulong, ulong)> keys)
    {
        var n = SqlNames.Get(db);

        // See above for explanation
        var sql = $"""
            DELETE FROM {n.TblUserEntry} AS t
            USING unnest(@pkey1, @pkey2) AS k(gid, uid)
            WHERE
                t.{n.UColGuildId} = k.gid AND t.{n.UColUserId} = k.uid
                AND (CURRENT_DATE - {DeleteThreshold}) > t.{n.UColLastSeen}
            """;
        return await db.Database.ExecuteSqlRawAsync(sql, Parameterize(keys));
    }

    // Converting to decimal to avoid certain database-side conversion errors
    private NpgsqlParameter<decimal[]>[] Parameterize(IEnumerable<(ulong, ulong)> keys) => [
            new NpgsqlParameter<decimal[]>
            {
                ParameterName = "pkey1",
                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Numeric,
                Value = keys.Select(k => (decimal)k.Item1).ToArray()
            },
            new NpgsqlParameter<decimal[]>
            {
                ParameterName = "pkey2",
                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Numeric,
                Value = keys.Select(k => (decimal)k.Item2).ToArray()
            }
        ];
    #endregion

}
