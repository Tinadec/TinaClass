using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinaClass.Api.Features.Agents.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agt_agent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Instruction = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    ModelCode = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    MaxSteps = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxCost = table.Column<long>(type: "INTEGER", nullable: false),
                    Visibility = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Schedule = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    RunCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SuccessCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agt_agent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agt_approval",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ToolIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    StepId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ArgumentsJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Risk = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    RequestedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecidedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DecidedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agt_approval", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agt_tool_server",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    LastSeenAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agt_tool_server", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agt_agent_base",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BaseId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agt_agent_base", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agt_agent_base_agt_agent_AgentId",
                        column: x => x.AgentId,
                        principalTable: "agt_agent",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agt_agent_tool",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToolId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Approval = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Sort = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agt_agent_tool", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agt_agent_tool_agt_agent_AgentId",
                        column: x => x.AgentId,
                        principalTable: "agt_agent",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agt_run",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    EndedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Tokens = table.Column<int>(type: "INTEGER", nullable: false),
                    Cost = table.Column<long>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Cursor = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agt_run", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agt_run_agt_agent_AgentId",
                        column: x => x.AgentId,
                        principalTable: "agt_agent",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agt_tool",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Risk = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Sort = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agt_tool", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agt_tool_agt_tool_server_ServerId",
                        column: x => x.ServerId,
                        principalTable: "agt_tool_server",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agt_run_step",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AtMs = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agt_run_step", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agt_run_step_agt_run_RunId",
                        column: x => x.RunId,
                        principalTable: "agt_run",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agt_agent_OwnerId",
                table: "agt_agent",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_agt_agent_base_AgentId_BaseId",
                table: "agt_agent_base",
                columns: new[] { "AgentId", "BaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agt_agent_tool_AgentId_ToolId",
                table: "agt_agent_tool",
                columns: new[] { "AgentId", "ToolId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agt_approval_RunId",
                table: "agt_approval",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_agt_approval_State_CreatedAt",
                table: "agt_approval",
                columns: new[] { "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_agt_run_AgentId_StartedAt",
                table: "agt_run",
                columns: new[] { "AgentId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_agt_run_State",
                table: "agt_run",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_agt_run_step_RunId_Ordinal",
                table: "agt_run_step",
                columns: new[] { "RunId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agt_tool_ServerId",
                table: "agt_tool",
                column: "ServerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agt_agent_base");

            migrationBuilder.DropTable(
                name: "agt_agent_tool");

            migrationBuilder.DropTable(
                name: "agt_approval");

            migrationBuilder.DropTable(
                name: "agt_run_step");

            migrationBuilder.DropTable(
                name: "agt_tool");

            migrationBuilder.DropTable(
                name: "agt_run");

            migrationBuilder.DropTable(
                name: "agt_tool_server");

            migrationBuilder.DropTable(
                name: "agt_agent");
        }
    }
}
