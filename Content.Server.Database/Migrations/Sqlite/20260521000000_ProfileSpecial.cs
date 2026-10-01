using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
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
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "special_perception",
                table: "profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "special_endurance",
                table: "profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "special_charisma",
                table: "profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "special_intelligence",
                table: "profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "special_agility",
                table: "profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "special_luck",
                table: "profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.Sql("""
                UPDATE profile
                SET
                    special_strength = COALESCE((
                        SELECT CASE WHEN c.strength BETWEEN 1 AND 10 THEN c.strength ELSE 5 END
                        FROM preference AS pref
                        JOIN character_player_data AS c ON c.player_id = pref.user_id
                        WHERE pref.preference_id = profile.preference_id
                            AND c.character_name = profile.char_name
                        LIMIT 1), special_strength),
                    special_perception = COALESCE((
                        SELECT CASE WHEN c.perception BETWEEN 1 AND 10 THEN c.perception ELSE 5 END
                        FROM preference AS pref
                        JOIN character_player_data AS c ON c.player_id = pref.user_id
                        WHERE pref.preference_id = profile.preference_id
                            AND c.character_name = profile.char_name
                        LIMIT 1), special_perception),
                    special_endurance = COALESCE((
                        SELECT CASE WHEN c.endurance BETWEEN 1 AND 10 THEN c.endurance ELSE 5 END
                        FROM preference AS pref
                        JOIN character_player_data AS c ON c.player_id = pref.user_id
                        WHERE pref.preference_id = profile.preference_id
                            AND c.character_name = profile.char_name
                        LIMIT 1), special_endurance),
                    special_charisma = COALESCE((
                        SELECT CASE WHEN c.charisma BETWEEN 1 AND 10 THEN c.charisma ELSE 5 END
                        FROM preference AS pref
                        JOIN character_player_data AS c ON c.player_id = pref.user_id
                        WHERE pref.preference_id = profile.preference_id
                            AND c.character_name = profile.char_name
                        LIMIT 1), special_charisma),
                    special_intelligence = COALESCE((
                        SELECT CASE WHEN c.intelligence BETWEEN 1 AND 10 THEN c.intelligence ELSE 5 END
                        FROM preference AS pref
                        JOIN character_player_data AS c ON c.player_id = pref.user_id
                        WHERE pref.preference_id = profile.preference_id
                            AND c.character_name = profile.char_name
                        LIMIT 1), special_intelligence),
                    special_agility = COALESCE((
                        SELECT CASE WHEN c.agility BETWEEN 1 AND 10 THEN c.agility ELSE 5 END
                        FROM preference AS pref
                        JOIN character_player_data AS c ON c.player_id = pref.user_id
                        WHERE pref.preference_id = profile.preference_id
                            AND c.character_name = profile.char_name
                        LIMIT 1), special_agility),
                    special_luck = COALESCE((
                        SELECT CASE WHEN c.luck BETWEEN 1 AND 10 THEN c.luck ELSE 5 END
                        FROM preference AS pref
                        JOIN character_player_data AS c ON c.player_id = pref.user_id
                        WHERE pref.preference_id = profile.preference_id
                            AND c.character_name = profile.char_name
                        LIMIT 1), special_luck)
                WHERE EXISTS (
                    SELECT 1
                    FROM preference AS pref
                    JOIN character_player_data AS c ON c.player_id = pref.user_id
                    WHERE pref.preference_id = profile.preference_id
                        AND c.character_name = profile.char_name);
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
