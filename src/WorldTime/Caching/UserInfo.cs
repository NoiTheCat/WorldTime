using System.Text;

namespace WorldTime.Caching;

public sealed record UserInfo {
    public ulong GuildId { get; init; }
    public ulong UserId { get; init; }
    public required string Username { get; init; }
    public string? GlobalName { get; init; }
    public string? GuildNickname { get; init; }
    public DateTimeOffset ItemAge { get; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Formats this user's name to a consistent, readable format which makes use of their nickname.
    /// </summary>
    public string FormatName() {
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
        var username = escapeFormattingCharacters(GlobalName ?? Username);
        if (GuildNickname != null) {
            return $"{escapeFormattingCharacters(GuildNickname)} ({username})";
        }
        return username;
    }
}
