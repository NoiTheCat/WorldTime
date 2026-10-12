namespace WorldTime.Data;

public class UserEntry {
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }

    public DateTimeZone TimeZone { get; set; } = null!;

    public Instant LastSeen { get; set; }
}
