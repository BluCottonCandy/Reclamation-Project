using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres;

public partial class CharacterConsent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "torture_consent", table: "profile", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "slavery_consent", table: "profile", type: "integer", nullable: false, defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "torture_consent", table: "profile");
        migrationBuilder.DropColumn(name: "slavery_consent", table: "profile");
    }
}
