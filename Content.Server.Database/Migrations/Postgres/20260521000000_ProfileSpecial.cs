using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class ProfileSpecial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "special_strength",
                table: "profile",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "special_perception",
                table: "profile",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "special_endurance",
                table: "profile",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "special_charisma",
                table: "profile",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "special_intelligence",
                table: "profile",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "special_agility",
                table: "profile",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "special_luck",
                table: "profile",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.Sql("""
                UPDATE profile AS p
                SET
                    special_strength = CASE WHEN c.strength BETWEEN 1 AND 10 THEN c.strength ELSE 5 END,
                    special_perception = CASE WHEN c.perception BETWEEN 1 AND 10 THEN c.perception ELSE 5 END,
                    special_endurance = CASE WHEN c.endurance BETWEEN 1 AND 10 THEN c.endurance ELSE 5 END,
                    special_charisma = CASE WHEN c.charisma BETWEEN 1 AND 10 THEN c.charisma ELSE 5 END,
                    special_intelligence = CASE WHEN c.intelligence BETWEEN 1 AND 10 THEN c.intelligence ELSE 5 END,
                    special_agility = CASE WHEN c.agility BETWEEN 1 AND 10 THEN c.agility ELSE 5 END,
                    special_luck = CASE WHEN c.luck BETWEEN 1 AND 10 THEN c.luck ELSE 5 END
                FROM preference AS pref
                JOIN character_player_data AS c ON c.player_id = pref.user_id
                WHERE p.preference_id = pref.preference_id
                    AND c.character_name = p.char_name;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "special_strength",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "special_perception",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "special_endurance",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "special_charisma",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "special_intelligence",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "special_agility",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "special_luck",
                table: "profile");
        }
    }
}
