using Microsoft.EntityFrameworkCore;
using NodaTime;
using NoiPublicBot.Common;
using WorldTime.Data;

namespace WorldTime.InteractionModules;

public class TzAutocompleteHandler : TimezoneAutocompleteBase {
    protected override async Task<IEnumerable<(DateTimeZone zone, int count)>> GetPopularityCountsAsync()
    {
        using var db = BotDatabaseContext.New();
        return (await db.UserEntries.AsNoTracking()
            .GroupBy(u => u.TimeZone)
            .Select(g => new { Zone = g.Key, Count = g.Count() })
            .ToListAsync().ConfigureAwait(false))
            .Select(s => ValueTuple.Create(s.Zone, s.Count)); // Cannot use ValueTuple in EF Core select (for now?)
    }
}
