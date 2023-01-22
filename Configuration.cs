using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace WorldTime;
/// <summary>
/// Loads and holds configuration values.
/// </summary>
class Configuration {
    public string BotToken { get; }
    public string? DBotsToken { get; }

    public int ShardTotal { get; }

    public string? SqlHost { get; }
    public string? SqlDatabase { get; }
    public string SqlUsername { get; }
    public string SqlPassword { get; }

    public Configuration() {
        var args = CommandLineParameters.Parse(Environment.GetCommandLineArgs());
        var path = args?.ConfigFile ?? Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)
            + Path.DirectorySeparatorChar + "." + Path.DirectorySeparatorChar + "settings.json";

        // Looks for configuration file
        JObject jc;
        try {
            var conftxt = File.ReadAllText(path);
            jc = JObject.Parse(conftxt);
        } catch (Exception ex) {
            string pfx;
            if (ex is JsonException) pfx = "Unable to parse configuration: ";
            else pfx = "Unable to access configuration: ";

            throw new Exception(pfx + ex.Message, ex);
        }

        BotToken = ReadConfKey<string>(jc, nameof(BotToken), true);
        DBotsToken = ReadConfKey<string>(jc, nameof(DBotsToken), false);

        ShardTotal = args.ShardTotal ?? ReadConfKey<int?>(jc, nameof(ShardTotal), false) ?? 1;
        if (ShardTotal < 1) throw new Exception($"'{nameof(ShardTotal)}' must be a positive integer.");

        SqlHost = ReadConfKey<string>(jc, nameof(SqlHost), false);
        SqlDatabase = ReadConfKey<string?>(jc, nameof(SqlDatabase), false);
        SqlUsername = ReadConfKey<string>(jc, nameof(SqlUsername), true);
        SqlPassword = ReadConfKey<string>(jc, nameof(SqlPassword), true);
    }

    private static T? ReadConfKey<T>(JObject jc, string key, [DoesNotReturnIf(true)] bool failOnEmpty) {
        if (jc.ContainsKey(key)) return jc[key]!.Value<T>();
        if (failOnEmpty) throw new Exception($"'{key}' must be specified.");
        return default;
    }

    class CommandLineParameters {
        [Option('c', "config")]
        public string? ConfigFile { get; set; }

        [Option("shardtotal")]
        public int? ShardTotal { get; set; }

        public static CommandLineParameters? Parse(string[] args) {
            CommandLineParameters? result = null;

            new Parser(settings => {
                settings.IgnoreUnknownArguments = true;
                settings.AutoHelp = false;
                settings.AutoVersion = false;
            }).ParseArguments<CommandLineParameters>(args)
                .WithParsed(p => result = p)
                .WithNotParsed(e => { /* ignore */ });
            return result;
        }
    }
}
