using Npgsql;
using NpgsqlTypes;

namespace WorldTime;

/// <summary>
/// Database abstractions
/// </summary>
public class Database {
    private const string UserDatabase = "userdata";

    private readonly string _connectionString;

    internal Database(string connectionString) {
        _connectionString = connectionString;
        DoInitialDatabaseSetupAsync().ConfigureAwait(false).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Sets up and opens a database connection.
    /// </summary>
    private async Task<NpgsqlConnection> OpenConnectionAsync() {
        var db = new NpgsqlConnection(_connectionString);
        await db.OpenAsync().ConfigureAwait(false);
        return db;
    }

    private async Task DoInitialDatabaseSetupAsync() {
        using var db = await OpenConnectionAsync().ConfigureAwait(false);
        using var c = db.CreateCommand();
        c.CommandText = $"create table if not exists {UserDatabase} ("
            + $"guild_id BIGINT, "
            + "user_id BIGINT, "
            + "zone TEXT NOT NULL, "
            + "last_active TIMESTAMPTZ NOT NULL DEFAULT now(), "
            + "PRIMARY KEY (guild_id, user_id)" // index automatically created with this
            + ")";
        await c.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Checks if a given guild contains at least one user data entry with recent enough activity.
    /// </summary>
    internal async Task<bool> HasAnyAsync(SocketGuild guild) {
        using var db = await OpenConnectionAsync().ConfigureAwait(false);
        using var c = db.CreateCommand();
        c.CommandText = $@"
SELECT true FROM {UserDatabase}
WHERE
    guild_id = @Gid
LIMIT 1
";
        c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)guild.Id;
        await c.PrepareAsync().ConfigureAwait(false);
        using var r = await c.ExecuteReaderAsync().ConfigureAwait(false);
        return await r.ReadAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the number of unique time zones in the database.
    /// </summary>
    internal async Task<int> GetDistinctZoneCountAsync() {
        using var db = await OpenConnectionAsync().ConfigureAwait(false);
        using var c = db.CreateCommand();
        c.CommandText = $"SELECT COUNT(DISTINCT zone) FROM {UserDatabase}";
        return (int)((long?)await c.ExecuteScalarAsync() ?? -1); // ExecuteScalarAsync returns a long here
    }

    /// <summary>
    /// Removes the specified user from the database.
    /// </summary>
    /// <returns>True if the removal was successful. False typically if the user did not exist.</returns>
    internal async Task<bool> DeleteUserAsync(SocketGuildUser user) {
        using var db = await OpenConnectionAsync().ConfigureAwait(false);
        using var c = db.CreateCommand();
        c.CommandText = $"DELETE FROM {UserDatabase} " +
            "WHERE guild_id = @Gid AND user_id = @Uid";
        c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)user.Guild.Id;
        c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = (long)user.Id;
        await c.PrepareAsync().ConfigureAwait(false);
        return await c.ExecuteNonQueryAsync().ConfigureAwait(false) > 0;
    }

    /// <summary>
    /// Inserts/updates the specified user in the database.
    /// </summary>
    internal async Task UpdateUserAsync(SocketGuildUser user, string timezone) {
        using var db = await OpenConnectionAsync().ConfigureAwait(false);
        using var c = db.CreateCommand();
        c.CommandText = $"INSERT INTO {UserDatabase} (guild_id, user_id, zone) " +
            "VALUES (@Gid, @Uid, @Zone) " +
            "ON CONFLICT (guild_id, user_id) DO " +
            "UPDATE SET zone = EXCLUDED.zone";
        c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)user.Guild.Id;
        c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = (long)user.Id;
        c.Parameters.Add("@Zone", NpgsqlDbType.Text).Value = timezone;
        await c.PrepareAsync().ConfigureAwait(false);
        await c.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the time zone name of a single user.
    /// </summary>
    internal async Task<string?> GetUserZoneAsync(SocketGuildUser user) {
        using var db = await OpenConnectionAsync().ConfigureAwait(false);
        using var c = db.CreateCommand();
        c.CommandText = $"SELECT zone FROM {UserDatabase} " +
            "WHERE guild_id = @Gid AND user_id = @Uid";
        c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)user.Guild.Id;
        c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = (long)user.Id;
        await c.PrepareAsync().ConfigureAwait(false);
        return (string?)await c.ExecuteScalarAsync();
    }

    /// <summary>
    /// Retrieves all known user time zones for the given guild.
    /// Further filtering should be handled by the consumer.
    /// </summary>
    /// <returns>
    /// An unsorted dictionary. Keys are time zones, values are user IDs representative of those zones.
    /// </returns>
    internal async Task<Dictionary<string, List<ulong>>> GetGuildZonesAsync(ulong guildId) {
        using var db = await OpenConnectionAsync().ConfigureAwait(false);
        using var c = db.CreateCommand();
        c.CommandText = $@" -- Simpler query than 1.x; most filtering is now done by caller
SELECT zone, user_id FROM {UserDatabase}
WHERE
    guild_id = @Gid
ORDER BY RANDOM() -- Randomize results for display purposes";
        c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)guildId;
        await c.PrepareAsync().ConfigureAwait(false);
        var r = await c.ExecuteReaderAsync().ConfigureAwait(false);

        var resultSet = new Dictionary<string, List<ulong>>();
        while (await r.ReadAsync().ConfigureAwait(false)) {
            var tz = r.GetString(0);
            var user = (ulong)r.GetInt64(1);

            if (!resultSet.ContainsKey(tz)) resultSet.Add(tz, new List<ulong>());
            resultSet[tz].Add(user);
        }
        return resultSet;
    }
}
