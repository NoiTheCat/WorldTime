using NoiPublicBot.Common;

namespace WorldTime.Localization;

static class StringProviders {
    public static LocalizedStringResolver Commands { get; }
    public static LocalizedStringResolver Responses { get; }

    static StringProviders() {
        Commands = new("Commands");
        Responses = new("Responses");
    }
}
