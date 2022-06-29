﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using WorldTime.Data;

#nullable disable

namespace WorldTime.Data.Migrations
{
    [DbContext(typeof(BotDatabaseContext))]
    partial class BotDatabaseContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "6.0.6")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("WorldTime.Data.UserEntry", b =>
                {
                    b.Property<long>("GuildId")
                        .HasColumnType("bigint")
                        .HasColumnName("guild_id");

                    b.Property<long>("UserId")
                        .HasColumnType("bigint")
                        .HasColumnName("user_id");

                    b.Property<DateTime>("LastUpdate")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("last_active")
                        .HasDefaultValueSql("now()");

                    b.Property<string>("TimeZone")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("zone");

                    b.HasKey("GuildId", "UserId")
                        .HasName("userdata_pkey");

                    b.ToTable("userdata", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}
