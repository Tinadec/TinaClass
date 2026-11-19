using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinaClass.Api.Features.Models.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mdl_provider",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    KeyCipher = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    KeyHint = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    LastCheckAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastLatencyMs = table.Column<int>(type: "INTEGER", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mdl_provider", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mdl_model",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ContextTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    PriceIn = table.Column<long>(type: "INTEGER", nullable: false),
                    PriceOut = table.Column<long>(type: "INTEGER", nullable: false),
                    PriceCache = table.Column<long>(type: "INTEGER", nullable: false),
                    Caps = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Efforts = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mdl_model", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mdl_model_mdl_provider_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "mdl_provider",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mdl_model_ProviderId_Code",
                table: "mdl_model",
                columns: new[] { "ProviderId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mdl_provider_SortOrder",
                table: "mdl_provider",
                column: "SortOrder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mdl_model");

            migrationBuilder.DropTable(
                name: "mdl_provider");
        }
    }
}
