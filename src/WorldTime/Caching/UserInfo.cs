using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace WorldTime.Caching;

public sealed record UserInfo {
    public ulong GuildId { get; private init; }
    public ulong UserId { get; private init; }

    [MemberNotNullWhen(false, nameof(IsNull))]
    public string? Username { get; private init; }
    public string? GlobalName { get; private init; }
    public string? GuildNickname { get; private init; }

    public DateTimeOffset EntryTTL { get; private init; }
    public bool IsNull { get; private init; }

    /// <summary>
    /// Formats this user's name to a consistent, readable format which makes use of their nickname.
    /// </summary>
    public string FormatName() {
        if (IsNull) throw new InvalidOperationException("This entry is incomplete and must be considered effectively null.");
        static string escapeFormattingCharacters(string input) {
            var result = new StringBuilder();
            foreach (var c in input) {
                if (c is '\\' or '_' or '~' or '*' or '@' or '`') {
                    result.Append('\\');
                }
                result.Append(c);
            }
            return result.ToString();
        }
        var username = escapeFormattingCharacters(GlobalName ?? Username!);
        if (GuildNickname != null) {
            return $"{escapeFormattingCharacters(GuildNickname)} ({username})";
        }
        return username;
    }

    #region Entry lifetime
    // Currently, record TTL varies from 1 to 6 hours
    private static readonly TimeSpan MinimumTTL = TimeSpan.FromMinutes(60);
    const int JitterMaxMinutes = 300;
    
    private static TimeSpan CalculateJitter() {
        var jitter = Program.JitterSource.Value!.Next(JitterMaxMinutes);
        return MinimumTTL + TimeSpan.FromMinutes(jitter);
    }

    public static UserInfo CreateFrom(IGuildUser user) => new() {
        GuildId = user.GuildId,
        UserId = user.Id,
        Username = user.Username,
        GlobalName = user.GlobalName,
        GuildNickname = user.Nickname,

        EntryTTL = DateTimeOffset.UtcNow + CalculateJitter(),
        IsNull = false
    };

    public static UserInfo NullFrom(ulong guildId, ulong userId) => new() {
        GuildId = guildId,
        UserId = userId,

        // Null results get a slightly higher lifetime
        EntryTTL = DateTimeOffset.UtcNow + (CalculateJitter() * 1.5),
        IsNull = true
    };
    #endregion
}
