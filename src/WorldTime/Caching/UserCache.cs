using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace WorldTime.Caching;

public class UserCache {
    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, UserInfo>> _cache = new();
    private readonly TimeSpan _ttl = TimeSpan.FromHours(6); // TODO modify this

    public void Update(UserInfo info) {
        var guild = _cache.GetOrAdd(info.GuildId, _ => new());
        guild[info.UserId] = info;
    }

    public bool TryGetGuildUsers(ulong guildId, [NotNullWhen(true)] out HashSet<ulong>? userIds) {
        userIds = null;
        if (!_cache.TryGetValue(guildId, out var uinfos)) return false;
        userIds = [.. uinfos.Keys];
        return true;
    }

    public bool TryGetUser(ulong guildId, ulong userId, [NotNullWhen(true)] out UserInfo? user) {
        user = null;
        if (!_cache.TryGetValue(guildId, out var g)) return false;
        if (!g.TryGetValue(userId, out var info)) return false;
        if (DateTimeOffset.UtcNow - info.ItemAge > _ttl) return false; // stale

        user = info;
        return true;
    }

    public void Sweep() {
        foreach (var k in _cache.Keys) {
            Sweep(k);
        }
    }

    public void Sweep(ulong guildId) {
        if (!_cache.TryGetValue(guildId, out var guild)) return;
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, entry) in guild) {
            if (now - entry.ItemAge > _ttl)
                guild.TryRemove(id, out _);
        }
        if (guild.IsEmpty) _cache.TryRemove(guildId, out _);
    }
}

/*
thoughts:
dictionary of dictionary of users
short ttl?

data sources:
incoming interactions (opportunistic module)
background filler task
manual fill by notif task

background task:
logic for upcoming birthdays, then fetch/refresh on imminent (several hours before?)
- consider TTL carefully for this alone
sweep after

consumers:
background birthday notification service
-it'll do a final sweep, filling in holes in cache. if still nonexist (oh - maybe flag nonexisting - null?)
*/