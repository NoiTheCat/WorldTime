using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace WorldTime.Caching;

public sealed class UserCache {
    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, UserInfo>> _cache = new();

    public void Update(UserInfo info) {
        var guild = _cache.GetOrAdd(info.GuildId, _ => new());
        guild[info.UserId] = info;
    }

    // For use when refreshing cache
    public IEnumerable<ulong> GetExistingGuildUsers(ulong guildId) {
        if (_cache.TryGetValue(guildId, out var uinfos)) {
            var now = DateTimeOffset.UtcNow;
            foreach (var (_, entry) in uinfos) {
                if (now < entry.EntryTTL) yield return entry.UserId;
            }
        }
        yield break;
    }

    public bool TryGetUser(ulong guildId, ulong userId, [NotNullWhen(true)] out UserInfo? user) {
        user = null;
        if (!_cache.TryGetValue(guildId, out var g)) return false;
        if (!g.TryGetValue(userId, out var info)) return false;
        if (DateTimeOffset.UtcNow > info.EntryTTL) return false; // stale

        user = info;
        return true;
    }

    public void Sweep(ulong guildId) {
        if (!_cache.TryGetValue(guildId, out var guild)) return;
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, entry) in guild) {
            if (now > entry.EntryTTL)
                guild.TryRemove(id, out _);
        }
        if (guild.IsEmpty) _cache.TryRemove(guildId, out _);
    }
}
