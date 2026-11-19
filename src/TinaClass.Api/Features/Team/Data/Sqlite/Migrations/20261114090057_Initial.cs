using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinaClass.Api.Features.Team.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "team_audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActorName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    TargetKind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    TargetId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    At = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_audit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "team_invite",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    RoleId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    InvitedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    AcceptedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_invite", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_team_audit_At",
                table: "team_audit",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_team_invite_NormalizedEmail",
                table: "team_invite",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_team_invite_TokenHash",
                table: "team_invite",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "team_audit");

            migrationBuilder.DropTable(
                name: "team_invite");
        }
    }
}
