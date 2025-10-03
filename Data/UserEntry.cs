using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorldTime.Data;
[Table("userdata")]
public class UserEntry {
    [Key]
    [Column("guild_id")]
    public ulong GuildId { get; set; }
    [Key]
    [Column("user_id")]
    public ulong UserId { get; set; }
    [Column("zone")]
    public string TimeZone { get; set; } = null!;
}