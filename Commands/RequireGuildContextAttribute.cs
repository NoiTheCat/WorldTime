using Discord.Interactions;

namespace WorldTime.Commands;
/// <summary>
/// Implements the included precondition from Discord.Net, requiring a guild context while using our custom error message.
/// </summary>
class RequireGuildContextAttribute : RequireContextAttribute {
    public const string Error = "Command not received within a guild context.";
    public const string Reply = ":x: This command is only available within a server.";

    public override string ErrorMessage => Error;

    public RequireGuildContextAttribute() : base(ContextType.Guild) { }
}
