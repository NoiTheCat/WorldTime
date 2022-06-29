using Microsoft.EntityFrameworkCore;

namespace WorldTime.Data;
public class BotDatabaseContext : DbContext {
    private static string? _npgsqlConnectionString;
    internal static string NpgsqlConnectionString {
#if DEBUG
        get {
            if (_npgsqlConnectionString != null) return _npgsqlConnectionString;
            Program.Log(nameof(BotDatabaseContext), "Using hardcoded connection string!");
            return _npgsqlConnectionString ?? "Host=localhost;Username=worldtime;Password=wt";
        }
#else
        get => _npgsqlConnectionString!;
#endif
        set => _npgsqlConnectionString ??= value;
    }

    public DbSet<UserEntry> UserEntries { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
         => optionsBuilder
            .UseNpgsql(NpgsqlConnectionString)
#if DEBUG
            .LogTo((string line) => Program.Log("EF", line), Microsoft.Extensions.Logging.LogLevel.Information)
#endif
            .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        modelBuilder.Entity<UserEntry>(entity => {
            entity.HasKey(e => new { e.GuildId, e.UserId }).HasName("userdata_pkey");
        });
    }

    #region Helper methods / abstractions
    /// <summary>
    /// Checks if a given guild contains at least one user data entry with recent enough activity.
    /// <br />To be used within a <see langword="using"/> context.
    /// </summary>
    internal bool HasAnyUsers(SocketGuild guild) => UserEntries.Where(u => u.GuildId == (long)guild.Id).Any();

    /// <summary>
    /// Gets the number of unique time zones in the database.
    /// <br />To be used within a <see langword="using"/> context.
    /// </summary>
    internal int GetDistinctZoneCount() => UserEntries.Select(u => u.TimeZone).Distinct().Count();

    /// <summary>
    /// Removes the specified user from the database.
    /// <br />To be used within a <see langword="using"/> context.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the removal was successful.
    /// <see langword="false"/> if the user did not exist.
    /// </returns>
    internal bool DeleteUser(SocketGuildUser user) {
        var tuser = UserEntries.Where(u => u.UserId == (long)user.Id && u.GuildId == (long)user.Guild.Id).SingleOrDefault();
        if (tuser != null) {
            Remove(tuser);
            SaveChanges();
            return true;
        } else {
            return false;
        }
    }

    /// <summary>
    /// Inserts/updates the specified user in the database.
    /// <br />To be used within a <see langword="using"/> context.
    /// </summary>
    internal void UpdateUser(SocketGuildUser user, string timezone) {
        var tuser = UserEntries.Where(u => u.UserId == (long)user.Id && u.GuildId == (long)user.Guild.Id).SingleOrDefault();
        if (tuser != null) {
            Update(tuser);
        } else {
            tuser = new UserEntry() { UserId = (long)user.Id, GuildId = (long)user.Guild.Id };
            Add(tuser);
        }
        tuser.TimeZone = timezone;
        SaveChanges();
    }

    /// <summary>
    /// Retrieves the time zone name of a single user.
    /// <br />To be used within a <see langword="using"/> context.
    /// </summary>
    internal string? GetUserZone(SocketGuildUser user) {
        var tuser = UserEntries.Where(u => u.UserId == (long)user.Id && u.GuildId == (long)user.Guild.Id).SingleOrDefault();
        return tuser?.TimeZone;
    }

    /// <summary>
    /// Retrieves all known user time zones for the given guild.
    /// <br />To be used within a <see langword="using"/> context.
    /// </summary>
    /// <returns>
    /// An unsorted dictionary. Keys are time zones, values are user IDs representative of those zones.
    /// </returns>
    internal Dictionary<string, List<ulong>> GetGuildZones(ulong guildId) {
        // Implementing the query from the previous iteration, in which further filtering is done by the caller.
        // TODO consider bringing filtering back to this step, if there may be any advantage
        var query = from entry in UserEntries
                    where entry.GuildId == (long)guildId
                    orderby entry.UserId
                    select Tuple.Create(entry.TimeZone, (ulong)entry.UserId);
        var resultSet = new Dictionary<string, List<ulong>>();
        foreach (var (tz, user) in query) {
            if (!resultSet.ContainsKey(tz)) resultSet.Add(tz, new List<ulong>());
            resultSet[tz].Add(user);
        }
        return resultSet;
    }
    #endregion
}
