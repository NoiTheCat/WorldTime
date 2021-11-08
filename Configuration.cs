using CommandLine;
using CommandLine.Text;
using Newtonsoft.Json.Linq;
using Npgsql;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace WorldTime;

/// <summary>
/// Loads and holds configuration values.
/// </summary>
class Configuration {
    const string KeySqlHost = "SqlHost";
    const string KeySqlUsername = "SqlUsername";
    const string KeySqlPassword = "SqlPassword";
    const string KeySqlDatabase = "SqlDatabase";

    public string DbConnectionString { get; }
    public string BotToken { get; }
    public string? DBotsToken { get; }

    public int ShardTotal { get; }

    public Configuration(string[] args) {
        var cmdline = CmdLineOpts.Parse(args);

        // Looks for configuration file
        var confPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + Path.DirectorySeparatorChar;
        confPath += cmdline.Config!;
        if (!File.Exists(confPath)) throw new Exception("Settings file not found in path: " + confPath);

        var jc = JObject.Parse(File.ReadAllText(confPath));

        BotToken = ReadConfKey<string>(jc, nameof(BotToken), true);
        DBotsToken = ReadConfKey<string>(jc, nameof(DBotsToken), false);

        ShardTotal = cmdline.ShardTotal ?? ReadConfKey<int?>(jc, nameof(ShardTotal), false) ?? 1;
        if (ShardTotal < 1) throw new Exception($"'{nameof(ShardTotal)}' must be a positive integer.");

        var sqlhost = ReadConfKey<string>(jc, KeySqlHost, false) ?? "localhost"; // Default to localhost
        var sqluser = ReadConfKey<string>(jc, KeySqlUsername, false);
        var sqlpass = ReadConfKey<string>(jc, KeySqlPassword, false);
        if (string.IsNullOrWhiteSpace(sqluser) || string.IsNullOrWhiteSpace(sqlpass))
            throw new Exception("'SqlUsername', 'SqlPassword' must be specified.");
        var csb = new NpgsqlConnectionStringBuilder() {
            Host = sqlhost,
            Username = sqluser,
            Password = sqlpass
        };
        var sqldb = ReadConfKey<string>(jc, KeySqlDatabase, false);
        if (sqldb != null) csb.Database = sqldb; // Optional database setting
        DbConnectionString = csb.ToString();
    }

    private static T? ReadConfKey<T>(JObject jc, string key, [DoesNotReturnIf(true)] bool failOnEmpty) {
        if (jc.ContainsKey(key)) return jc[key]!.Value<T>();
        if (failOnEmpty) throw new Exception($"'{key}' must be specified.");
        return default;
    }

    private class CmdLineOpts {
        [Option('c', "config", Default = "settings.json",
            HelpText = "Custom path to instance configuration, relative from executable directory.")]
        public string? Config { get; set; }

        [Option("shardtotal",
            HelpText = "Total number of shards online. MUST be the same for all instances.\n"
            + "This value overrides the config file value.")]
        public int? ShardTotal { get; set; }

        public static CmdLineOpts Parse(string[] args) {
            // Do not automatically print help message
            var clp = new Parser(c => c.HelpWriter = null);

            CmdLineOpts? result = null;
            var r = clp.ParseArguments<CmdLineOpts>(args);
            r.WithParsed(parsed => result = parsed);
            r.WithNotParsed(err => {
                var ht = HelpText.AutoBuild(r);
                Console.WriteLine(ht.ToString());
                Environment.Exit((int)Program.ExitCodes.BadCommand);
            });
            return result!;
        }
    }
}
