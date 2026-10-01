using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <summary>
    /// #Misfits Change - Add tables for persistent player data, entities, tiles, decals, and ATM placements.
    /// Converts remaining JSON-file persistence systems to database storage.
    /// </summary>
    public partial class MisfitsPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── character_player_data ──────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "character_player_data",
                columns: table => new
                {
                    character_player_data_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    character_name = table.Column<string>(type: "text", nullable: false),
                    strength = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    perception = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    endurance = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    charisma = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    intelligence = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    agility = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    luck = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    mob_kills = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    deaths = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    rounds_played = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    stats_confirmed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    history_log = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_player_data", x => x.character_player_data_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_character_player_data_player_id_character_name",
                table: "character_player_data",
                columns: new[] { "player_id", "character_name" },
                unique: true);

            // ── persistent_entity ─────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "persistent_entity",
                columns: table => new
                {
                    persistent_entity_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    persistence_id = table.Column<string>(type: "text", nullable: false),
                    prototype_id = table.Column<string>(type: "text", nullable: false),
                    x = table.Column<float>(type: "real", nullable: false),
                    y = table.Column<float>(type: "real", nullable: false),
                    rotation_degrees = table.Column<double>(type: "double precision", nullable: false),
                    spawned_by = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_persistent_entity", x => x.persistent_entity_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_persistent_entity_persistence_id",
                table: "persistent_entity",
                column: "persistence_id",
                unique: true);

            // ── persistent_tile ───────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "persistent_tile",
                columns: table => new
                {
                    persistent_tile_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    persistence_id = table.Column<string>(type: "text", nullable: false),
                    tile_def_name = table.Column<string>(type: "text", nullable: false),
                    x = table.Column<float>(type: "real", nullable: false),
                    y = table.Column<float>(type: "real", nullable: false),
                    rotation_mirroring = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    spawned_by = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_persistent_tile", x => x.persistent_tile_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_persistent_tile_persistence_id",
                table: "persistent_tile",
                column: "persistence_id",
                unique: true);

            // ── persistent_decal ──────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "persistent_decal",
                columns: table => new
                {
                    persistent_decal_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    persistence_id = table.Column<string>(type: "text", nullable: false),
                    decal_id = table.Column<string>(type: "text", nullable: false),
                    x = table.Column<float>(type: "real", nullable: false),
                    y = table.Column<float>(type: "real", nullable: false),
                    rotation = table.Column<float>(type: "real", nullable: false),
                    color_argb = table.Column<int>(type: "integer", nullable: false),
                    z_index = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    cleanable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    spawned_by = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_persistent_decal", x => x.persistent_decal_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_persistent_decal_persistence_id",
                table: "persistent_decal",
                column: "persistence_id",
                unique: true);

            // ── atm_placement ─────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "atm_placement",
                columns: table => new
                {
                    atm_placement_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    placement_key = table.Column<string>(type: "text", nullable: false),
                    prototype_id = table.Column<string>(type: "text", nullable: false),
                    map_name = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    tile_x = table.Column<int>(type: "integer", nullable: false),
                    tile_y = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_atm_placement", x => x.atm_placement_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_atm_placement_placement_key",
                table: "atm_placement",
                column: "placement_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "character_player_data");
            migrationBuilder.DropTable(name: "persistent_entity");
            migrationBuilder.DropTable(name: "persistent_tile");
            migrationBuilder.DropTable(name: "persistent_decal");
            migrationBuilder.DropTable(name: "atm_placement");
        }
    }
}
