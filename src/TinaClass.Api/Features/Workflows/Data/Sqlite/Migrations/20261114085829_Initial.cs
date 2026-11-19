using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinaClass.Api.Features.Workflows.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wf_flow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    DraftJson = table.Column<string>(type: "TEXT", maxLength: 100000, nullable: false),
                    PublishedVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wf_flow", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "wf_flow_version",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FlowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    GraphJson = table.Column<string>(type: "TEXT", maxLength: 100000, nullable: false),
                    PublishedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wf_flow_version", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wf_flow_version_wf_flow_FlowId",
                        column: x => x.FlowId,
                        principalTable: "wf_flow",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wf_run",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FlowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FlowVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Input = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    StartedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    EndedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Cost = table.Column<long>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wf_run", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wf_run_wf_flow_FlowId",
                        column: x => x.FlowId,
                        principalTable: "wf_flow",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wf_node_run",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NodeKey = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    StartMs = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Output = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wf_node_run", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wf_node_run_wf_run_RunId",
                        column: x => x.RunId,
                        principalTable: "wf_run",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wf_flow_version_FlowId_Number",
                table: "wf_flow_version",
                columns: new[] { "FlowId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wf_node_run_RunId_NodeKey",
                table: "wf_node_run",
                columns: new[] { "RunId", "NodeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wf_run_FlowId_StartedAt",
                table: "wf_run",
                columns: new[] { "FlowId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_wf_run_Ordinal",
                table: "wf_run",
                column: "Ordinal",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wf_flow_version");

            migrationBuilder.DropTable(
                name: "wf_node_run");

            migrationBuilder.DropTable(
                name: "wf_run");

            migrationBuilder.DropTable(
                name: "wf_flow");
        }
    }
}
