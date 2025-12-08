using CommandLine;

namespace WorldTime;
class CmdlineParser {
    [Option('c', "config")]
    public string? ConfigFile { get; set; }

    [Option("shardtotal")]
    public int? ShardTotal { get; set; }

    [Option("shardrange")]
    public string? ShardRange { get; set; }

#if AOT
    // Explicit public constructor
    public CmdlineParser() { }
#endif

    internal static CmdlineParser? Parse(string[] args) {
        CmdlineParser? result = null;

        new Parser(settings => {
            settings.IgnoreUnknownArguments = true;
            settings.AutoHelp = false;
            settings.AutoVersion = false;
        }).ParseArguments<CmdlineParser>(args)
            .WithParsed(p => result = p)
            .WithNotParsed(e => { /* ignore */ });
        return result;
    }
}