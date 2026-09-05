using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NoiPublicBot.BackgroundServices;
using NoiPublicBot.Common.UserCache;
using Npgsql;
using WorldTime.Data;

namespace WorldTime.BackgroundServices;

// Keeps track of known existing users. Removes old unused data
sealed class DataJanitor : BackgroundService
{
    /*
    Problem: We can't know if users are actually currently available.
    Goal: Do not have any cache fetching requests originate from this service.

    To accomplish this and prevent accidental deletions, DataJanitor will only
    work on users already loaded in cache. Elsewhere, filtering will be done to
    exclude database information from users who were not seen past the threshold.

    This rewrite is very similar to the DataJanitor in BirthdayBot, but cannot assume
    that user entries will always be made available automatically. Guilds may be deleted
    past the threshold, but the question of how to deal with users is as of yet uncertain.
    */

    // Process about once every six hours
    private static readonly Duration ProcessInterval = Duration.FromHours(6);
    // First run to be processed a few minutes after initialization
    private Instant _lastRun = SystemClock.Instance.GetCurrentInstant() - (ProcessInterval - Duration.FromMinutes(10));

    // Amount of days without updates before data is considered stale.
    public const int DeleteThreshold = 90;

    public override async Task OnTick(int tickCount, CancellationToken token)
    {
        var sinceLast = SystemClock.Instance.GetCurrentInstant() - _lastRun;
        if (sinceLast < ProcessInterval)
        {
            Log.Verbose("Interval not yet reached ({TimeSinceLast}). Not proceeding.", sinceLast);
            return;
        }

        using var db = BotDatabaseContext.New();
        // For more complete logging, checking users before guilds
        await UpdateUsersAsync(db);
        await UpdateGuildsAsync(db);
        if (Shard.ShardId == 0) await DeleteGuildsAsync(db); // Shard 0 only: drop old guild records
        _lastRun = SystemClock.Instance.GetCurrentInstant();
    }

    private async Task UpdateGuildsAsync(BotDatabaseContext db)
    {
        var today = SystemClock.Instance.GetCurrentInstant().InUtc().Date;
        var cutoff = today - Period.FromDays(DeleteThreshold);

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

    private async Task UpdateUsersAsync(BotDatabaseContext db)
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

        // Present users: update LastSeen
        var updateCt = await RawUpdateUsersAsync(db, present).ConfigureAwait(false);
        Log.Information("Updated {UpdatedUsers} user records.", updateCt);
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
