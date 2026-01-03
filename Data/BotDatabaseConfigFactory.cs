using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using WorldTime.Config;

namespace WorldTime.Data;
public class BotDatabaseContextFactory : IDesignTimeDbContextFactory<BotDatabaseContext> {
    // Used by EF Core tools for migrations, etc.
    public BotDatabaseContext CreateDbContext(string[] args) {
        var conf = new ConfigurationLoader(args);

        var opts = new DbContextOptionsBuilder<BotDatabaseContext>();
        opts.UseNpgsql(conf.GetConnectionString());
        opts.UseSnakeCaseNamingConvention();

        return new BotDatabaseContext(opts.Options);
    }
}
