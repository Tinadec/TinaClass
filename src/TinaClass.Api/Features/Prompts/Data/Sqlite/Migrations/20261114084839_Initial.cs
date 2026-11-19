using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinaClass.Api.Features.Prompts.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pmt_prompt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Command = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    Visibility = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SystemText = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    UserText = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    UseCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pmt_prompt", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pmt_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PromptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Input = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Expect = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    LastPassed = table.Column<bool>(type: "INTEGER", nullable: true),
                    Sort = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pmt_case", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pmt_case_pmt_prompt_PromptId",
                        column: x => x.PromptId,
                        principalTable: "pmt_prompt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pmt_version",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PromptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SystemText = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    UserText = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pmt_version", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pmt_version_pmt_prompt_PromptId",
                        column: x => x.PromptId,
                        principalTable: "pmt_prompt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pmt_case_PromptId",
                table: "pmt_case",
                column: "PromptId");

            migrationBuilder.CreateIndex(
                name: "IX_pmt_prompt_Command",
                table: "pmt_prompt",
                column: "Command",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pmt_prompt_OwnerId",
                table: "pmt_prompt",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_pmt_version_PromptId_Number",
                table: "pmt_version",
                columns: new[] { "PromptId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pmt_case");

            migrationBuilder.DropTable(
                name: "pmt_version");

            migrationBuilder.DropTable(
                name: "pmt_prompt");
        }
    }
}
