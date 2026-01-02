using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WorldTime.Data;
public class BotDatabaseContextFactory : IDesignTimeDbContextFactory<BotDatabaseContext> {
    // Used by EF Core tools for migrations, etc.
    public BotDatabaseContext CreateDbContext(string[] args) {
        // ignore args parameter - Configuration constructor handles it
        var conf = new Configuration();

        var opts = new DbContextOptionsBuilder<BotDatabaseContext>();
        opts.UseNpgsql(conf.SqlConnectionString);
        opts.UseSnakeCaseNamingConvention();

        return new BotDatabaseContext(opts.Options);
    }
}
