using System.ComponentModel;
using Newtonsoft.Json;

namespace WorldTime.Config;

public class DatabaseSettings {
    [Description("The host name, and optionally port, on which the PostgreSQL database is running.")]
    [DefaultValue("localhost")]
    public string Host { get; set; } = "localhost";

    [Description("The PosgreSQL to connect to. If left blank, the username is used.")]
    public string? Database { get; set; } = null;

    [JsonRequired]
    [Description("The PostgreSQL username to connect with.")]
    public string Username { get; set; } = null!;

    [JsonRequired]
    [Description("The password for the specified PostgreSQL user.")]
    public string Password { get; set; } = null!;

    internal void Validate() { }
}
