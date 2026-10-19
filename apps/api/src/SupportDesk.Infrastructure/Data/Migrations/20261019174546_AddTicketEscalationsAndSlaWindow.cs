using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupportDesk.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketEscalationsAndSlaWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SlaWindowSeconds",
                table: "Tickets",
                type: "int",
                nullable: true);

            // Existing tickets have never been escalated, so each one's window started when it was
            // created. Backfilled before CK_Tickets_SlaWindow, which requires it wherever a due date is set.
            migrationBuilder.Sql(
                """
                UPDATE [Tickets]
                SET [SlaWindowSeconds] = DATEDIFF(second, [CreatedAtUtc], [DueAtUtc])
                WHERE [DueAtUtc] IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "TicketEscalations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketId = table.Column<int>(type: "int", nullable: false),
                    FromPriority = table.Column<int>(type: "int", nullable: false),
                    ToPriority = table.Column<int>(type: "int", nullable: false),
                    FromAgentId = table.Column<int>(type: "int", nullable: true),
                    ToAgentId = table.Column<int>(type: "int", nullable: true),
                    FromDueAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ToDueAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EscalatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EscalatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketEscalations", x => x.Id);
                    table.CheckConstraint("CK_TicketEscalations_FromPriority", "[FromPriority] BETWEEN 1 AND 4");
                    table.CheckConstraint("CK_TicketEscalations_OneLevel", "[ToPriority] = [FromPriority] + 1");
                    table.CheckConstraint("CK_TicketEscalations_ToPriority", "[ToPriority] BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_TicketEscalations_Agents_FromAgentId",
                        column: x => x.FromAgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketEscalations_Agents_ToAgentId",
                        column: x => x.ToAgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketEscalations_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tickets_SlaWindow",
                table: "Tickets",
                sql: "([DueAtUtc] IS NULL AND [SlaWindowSeconds] IS NULL) OR ([DueAtUtc] IS NOT NULL AND [SlaWindowSeconds] > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_TicketEscalations_FromAgentId",
                table: "TicketEscalations",
                column: "FromAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketEscalations_TicketId_EscalatedAtUtc",
                table: "TicketEscalations",
                columns: new[] { "TicketId", "EscalatedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_TicketEscalations_ToAgentId",
                table: "TicketEscalations",
                column: "ToAgentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TicketEscalations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tickets_SlaWindow",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "SlaWindowSeconds",
                table: "Tickets");
        }
    }
}
