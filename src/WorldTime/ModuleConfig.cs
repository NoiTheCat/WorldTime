using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoiPublicBot;
using NoiPublicBot.Common.UserCache;
using WorldTime.BackgroundServices;
using WorldTime.Data;

public class ModuleConfig : ModuleConfigBase {
    public override IEnumerable<Type> BackgroundServices => [
        typeof(DataJanitor),
        typeof(CacheRefresher)
    ];

    public override void PreShardSetup(ref IServiceCollection services) {
        services.AddSingleton(s => new UserCache<BotDatabaseContext>(s.GetRequiredService<ShardInstance>()));
        services.AddDbContext<BotDatabaseContext>(opts => opts
            .UseNpgsql(Instance.SqlConnectionString.ConnectionString,
            npgopts => npgopts.UseNodaTime())
            .UseSnakeCaseNamingConvention());
    }

    public override void PostShardSetup(ShardInstance shard) {
        shard.OnStatusCheck += () => {
            var c = shard.LocalServices.GetRequiredService<UserCache<BotDatabaseContext>>();
            return $"Cache: {c.GuildsCount:0000} guilds -> {c.UsersCount:00,000} users.";
        };
    }

    // Surely I won't forget later on that I stuck this in here?
    internal static UserCache<BotDatabaseContext>.CacheFetchFilter FilterAllMissing => (cache, context, guildId) => {
        IEnumerable<ulong> local;
        var existing = cache.GetGuild(guildId, true);
        if (existing == null) local = [];
        else local = existing.Select(e => e.Value.UserId);

        var remote = context.UserEntries
            .Where(e => e.GuildId == guildId)
            .Select(e => e.UserId)
            .ToList();

        return [.. remote.Except(local)];
    };

    public override ILocalizationManager? LocalizationManager
        => new JsonLocalizationManager("Localization", "Commands");

    public override Func<string, string> GenericErrorProvider
        => loc => StringProviders.Responses.Get(loc, "errGeneric");

}
