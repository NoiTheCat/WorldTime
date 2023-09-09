using System.ComponentModel.DataAnnotations;

namespace WorldTime.Data;
public class GuildConfiguration {
    [Key]
    public ulong GuildId { get; set; }

    public bool Use12HourTime { get; set; }

    public bool EphemeralConfirm { get; set; }
}