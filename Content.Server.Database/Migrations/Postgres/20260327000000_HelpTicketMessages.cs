using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <summary>
    /// #Misfits Add - Add help_ticket_message table so individual bwoink/mhelp chat messages are
    /// persisted to the database and can be replayed from the Audit Log window across all past rounds.
    /// </summary>
    public partial class HelpTicketMessages : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "help_ticket_message",
                columns: table => new
                {
                    help_ticket_message_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<int>(type: "integer", nullable: false),
                    ticket_type = table.Column<int>(type: "integer", nullable: false),
                    sender_name = table.Column<string>(type: "text", nullable: false),
                    sender_is_staff = table.Column<bool>(type: "boolean", nullable: false),
                    message_text = table.Column<string>(type: "text", nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_help_ticket_message", x => x.help_ticket_message_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_help_ticket_message_player_id",
                table: "help_ticket_message",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "IX_help_ticket_message_ticket_id_ticket_type",
                table: "help_ticket_message",
                columns: new[] { "ticket_id", "ticket_type" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "help_ticket_message");
        }
    }
}
