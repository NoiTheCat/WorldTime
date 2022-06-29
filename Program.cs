﻿namespace WorldTime;

class Program {
    private static WorldTime? _bot;
    private static readonly DateTimeOffset _botStartTime = DateTimeOffset.UtcNow;

    /// <summary>
    /// Returns the amount of time the program has been running in a human-readable format.
    /// </summary>
    public static string BotUptime => (DateTimeOffset.UtcNow - _botStartTime).ToString("d' days, 'hh':'mm':'ss");

    static async Task Main(string[] args) {
        Configuration? cfg = null;
        try {
            cfg = new Configuration(args);
        } catch (Exception ex) {
            Console.WriteLine(ex);
            Environment.Exit((int)ExitCodes.ConfigError);
        }

        Data.BotDatabaseContext.NpgsqlConnectionString = cfg.DbConnectionString;

        Console.CancelKeyPress += OnCancelKeyPressed;
        _bot = new WorldTime(cfg);
        await _bot.StartAsync().ConfigureAwait(false);

        await Task.Delay(-1).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a formatted message to console.
    /// </summary>
    public static void Log(string source, string message) {
        var ts = DateTime.UtcNow;
        var ls = new string[] { "\r\n", "\n" };
        foreach (var item in message.Split(ls, StringSplitOptions.None))
            Console.WriteLine($"{ts:u} [{source}] {item}");
    }

    private static void OnCancelKeyPressed(object? sender, ConsoleCancelEventArgs e) {
        e.Cancel = true;
        Log("Shutdown", "Captured cancel key; sending shutdown.");
        ProgramStop();
    }

    private static bool _stopping = false;
    public static void ProgramStop() {
        if (_stopping) return;
        _stopping = true;
        Log("Shutdown", "Commencing shutdown...");

        var dispose = Task.Run(_bot!.Dispose);
        if (!dispose.Wait(90000)) {
            Log("Shutdown", "Normal shutdown has not concluded after 90 seconds. Will force quit.");
            Environment.ExitCode &= (int)ExitCodes.ForcedExit;
        }
        Environment.Exit(Environment.ExitCode);
    }

    [Flags]
    public enum ExitCodes {
        Normal = 0x0,
        ForcedExit = 0x1,
        ConfigError = 0x2,
        DatabaseError = 0x4,
        DeadShardThreshold = 0x8,
        BadCommand = 0x10,
    }
}
