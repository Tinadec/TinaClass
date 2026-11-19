using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinaClass.Api.Features.Usage.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "use_budget",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ScopeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Period = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    WarnAt = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AlertMonth = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    AlertState = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_use_budget", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "use_daily",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Day = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ModelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ModelName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Requests = table.Column<int>(type: "INTEGER", nullable: false),
                    Tokens = table.Column<long>(type: "INTEGER", nullable: false),
                    CacheTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    FirstTokenMsTotal = table.Column<long>(type: "INTEGER", nullable: false),
                    Cost = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_use_daily", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "use_record",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModelId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProviderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModelName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SubjectKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokensIn = table.Column<int>(type: "INTEGER", nullable: false),
                    TokensOut = table.Column<int>(type: "INTEGER", nullable: false),
                    TokensCache = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstTokenMs = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Cost = table.Column<long>(type: "INTEGER", nullable: false),
                    PriceSnapshot = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    At = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_use_record", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_use_budget_Scope_ScopeId",
                table: "use_budget",
                columns: new[] { "Scope", "ScopeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_use_daily_Day_ModelId_UserId",
                table: "use_daily",
                columns: new[] { "Day", "ModelId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_use_record_At",
                table: "use_record",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_use_record_UserId_At",
                table: "use_record",
                columns: new[] { "UserId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "use_budget");

            migrationBuilder.DropTable(
                name: "use_daily");

            migrationBuilder.DropTable(
                name: "use_record");
        }
    }
}
