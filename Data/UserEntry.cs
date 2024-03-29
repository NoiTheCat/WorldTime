using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorldTime.Data;
[Table("userdata")]
public class UserEntry {
    [Key]
    [Column("guild_id")]
    public long GuildId { get; set; }
    [Key]
    [Column("user_id")]
    public long UserId { get; set; }
    [Column("zone")]
    public string TimeZone { get; set; } = null!;
}