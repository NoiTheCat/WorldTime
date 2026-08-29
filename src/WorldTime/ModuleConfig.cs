using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoiPublicBot;
using NoiPublicBot.Common.UserCache;
using Serilog.Events;
using WorldTime.BackgroundServices;
using WorldTime.Data;
using WorldTime.Localization;

public class ModuleConfig : ModuleConfigBase
{
    public override IEnumerable<Type> BackgroundServices => [
        typeof(DataJanitor),
        typeof(CacheRefresher)
    ];

    public override void PreShardSetup(ref IServiceCollection services)
    {
        services.AddSingleton(
            s => new UserCache<BotDatabaseContext>(
                s.GetRequiredService<ShardInstance>(), new EFWarmCacheProvider(BotDatabaseContext.New)));
        services.AddDbContext<BotDatabaseContext>(opts => opts
            .UseNpgsql(Instance.SqlConnectionString, npgopts => npgopts.UseNodaTime()));
    }

    public override IEnumerable<(LogEventLevel log, string message, object?[]? propertyValues)> StatusMessages(ShardInstance shard)
    {
        // TODO could be implemented in Core generically (such as "return UserCache.CreateStatusMessage")
        var c = shard.LocalServices.GetRequiredService<UserCache<BotDatabaseContext>>();
        return [(LogEventLevel.Information, "Cache[g:{CachedGuildsCount:000} u:{CachedUsersCount:0000}]", [c.GuildsCount, c.UsersCount])];
    }

    // Surely I won't forget later on that I stuck this in here?
    internal static UserCache<BotDatabaseContext>.AsyncCacheFetchFilter FilterAllMissing =>
        async (cache, context, guildId) =>
    {
        IEnumerable<ulong> local;
        var existing = cache.GetGuild(guildId, true);
        if (existing == null) local = [];
        else local = existing.Select(e => e.Value.UserId);

        var remote = await context.UserEntries
            .Where(e => e.GuildId == guildId)
            .Select(e => e.UserId)
            .ToListAsync().ConfigureAwait(false);

        return [.. remote.Except(local)];
    };

    public override ILocalizationManager? LocalizationManager
        => new JsonLocalizationManager("Localization", "Commands");

    public override Func<string, string> GenericErrorProvider
        => loc => StringProviders.Responses.Get(loc, "errGeneric");

    public override DbContext? StartupMigrationsDbContext => BotDatabaseContext.New();
}
