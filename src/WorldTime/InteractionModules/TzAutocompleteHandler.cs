using Microsoft.EntityFrameworkCore;
using NodaTime;
using NoiPublicBot.Common;
using WorldTime.Data;

namespace WorldTime.InteractionModules;

public class TzAutocompleteHandler : TimezoneAutocompleteBase {
    protected override List<(DateTimeZone zone, int count)> GetPopularityCounts() {
        using var db = BotDatabaseContext.New();
        return [.. db.UserEntries.AsNoTracking()
            .GroupBy(u => u.TimeZone)
            .Select(g => new { Zone = g.Key, Count = g.Count() })
            .AsEnumerable()
            .Select(i => (i.Zone, i.Count))];
    }
}
