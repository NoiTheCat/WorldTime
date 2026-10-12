using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace WorldTime.Data.Migrations
{
    /// <inheritdoc />
    public partial class Update2609 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_warm_cache",
                table: "warm_cache");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_entries",
                table: "user_entries");

            migrationBuilder.DropPrimaryKey(
                name: "pk_guild_settings",
                table: "guild_settings");

            migrationBuilder.RenameTable(
                name: "warm_cache",
                newName: "WarmCache");

            migrationBuilder.RenameTable(
                name: "user_entries",
                newName: "UserEntries");

            migrationBuilder.RenameTable(
                name: "guild_settings",
                newName: "GuildSettings");

            migrationBuilder.RenameColumn(
                name: "data",
                table: "WarmCache",
                newName: "Data");

            migrationBuilder.RenameColumn(
                name: "expires_at",
                table: "WarmCache",
                newName: "ExpiresAt");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "WarmCache",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "guild_id",
                table: "WarmCache",
                newName: "GuildId");

            migrationBuilder.RenameColumn(
                name: "time_zone",
                table: "UserEntries",
                newName: "TimeZone");

            migrationBuilder.RenameColumn(
                name: "last_seen",
                table: "UserEntries",
                newName: "LastSeen");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "UserEntries",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "guild_id",
                table: "UserEntries",
                newName: "GuildId");

            migrationBuilder.RenameIndex(
                name: "ix_user_entries_guild_id",
                table: "UserEntries",
                newName: "IX_UserEntries_GuildId");

            migrationBuilder.RenameColumn(
                name: "use12hour_time",
                table: "GuildSettings",
                newName: "Use12HourTime");

            migrationBuilder.RenameColumn(
                name: "ephemeral_confirm",
                table: "GuildSettings",
                newName: "EphemeralConfirm");

            migrationBuilder.RenameColumn(
                name: "guild_id",
                table: "GuildSettings",
                newName: "GuildId");

            migrationBuilder.AddColumn<LocalDate>(
                name: "LastSeen",
                table: "GuildSettings",
                type: "date",
                nullable: false,
                defaultValue: new NodaTime.LocalDate(1, 1, 1));

            migrationBuilder.AddPrimaryKey(
                name: "PK_WarmCache",
                table: "WarmCache",
                columns: new[] { "GuildId", "UserId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserEntries",
                table: "UserEntries",
                columns: new[] { "GuildId", "UserId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_GuildSettings",
                table: "GuildSettings",
                column: "GuildId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_WarmCache",
                table: "WarmCache");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserEntries",
                table: "UserEntries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GuildSettings",
                table: "GuildSettings");

            migrationBuilder.DropColumn(
                name: "LastSeen",
                table: "GuildSettings");

            migrationBuilder.RenameTable(
                name: "WarmCache",
                newName: "warm_cache");

            migrationBuilder.RenameTable(
                name: "UserEntries",
                newName: "user_entries");

            migrationBuilder.RenameTable(
                name: "GuildSettings",
                newName: "guild_settings");

            migrationBuilder.RenameColumn(
                name: "Data",
                table: "warm_cache",
                newName: "data");

            migrationBuilder.RenameColumn(
                name: "ExpiresAt",
                table: "warm_cache",
                newName: "expires_at");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "warm_cache",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "GuildId",
                table: "warm_cache",
                newName: "guild_id");

            migrationBuilder.RenameColumn(
                name: "TimeZone",
                table: "user_entries",
                newName: "time_zone");

            migrationBuilder.RenameColumn(
                name: "LastSeen",
                table: "user_entries",
                newName: "last_seen");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "user_entries",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "GuildId",
                table: "user_entries",
                newName: "guild_id");

            migrationBuilder.RenameIndex(
                name: "IX_UserEntries_GuildId",
                table: "user_entries",
                newName: "ix_user_entries_guild_id");

            migrationBuilder.RenameColumn(
                name: "Use12HourTime",
                table: "guild_settings",
                newName: "use12hour_time");

            migrationBuilder.RenameColumn(
                name: "EphemeralConfirm",
                table: "guild_settings",
                newName: "ephemeral_confirm");

            migrationBuilder.RenameColumn(
                name: "GuildId",
                table: "guild_settings",
                newName: "guild_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_warm_cache",
                table: "warm_cache",
                columns: new[] { "guild_id", "user_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_entries",
                table: "user_entries",
                columns: new[] { "guild_id", "user_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_guild_settings",
                table: "guild_settings",
                column: "guild_id");
        }
    }
}
