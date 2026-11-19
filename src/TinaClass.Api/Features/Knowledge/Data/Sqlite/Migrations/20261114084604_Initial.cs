using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinaClass.Api.Features.Knowledge.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "kb_base",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    EmbeddingModelCode = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ChunkSize = table.Column<int>(type: "INTEGER", nullable: false),
                    ChunkOverlap = table.Column<int>(type: "INTEGER", nullable: false),
                    Hybrid = table.Column<bool>(type: "INTEGER", nullable: false),
                    Rerank = table.Column<bool>(type: "INTEGER", nullable: false),
                    ThresholdPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    TopK = table.Column<int>(type: "INTEGER", nullable: false),
                    Visibility = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kb_base", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "kb_document",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    Mime = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Progress = table.Column<int>(type: "INTEGER", nullable: false),
                    ChunkCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Pinned = table.Column<bool>(type: "INTEGER", nullable: false),
                    UploaderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 200000, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kb_document", x => x.Id);
                    table.ForeignKey(
                        name: "FK_kb_document_kb_base_BaseId",
                        column: x => x.BaseId,
                        principalTable: "kb_base",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "kb_chunk",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Page = table.Column<int>(type: "INTEGER", nullable: true),
                    Heading = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    TokenCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kb_chunk", x => x.Id);
                    table.ForeignKey(
                        name: "FK_kb_chunk_kb_document_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "kb_document",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_kb_base_OwnerId",
                table: "kb_base",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_kb_chunk_BaseId",
                table: "kb_chunk",
                column: "BaseId");

            migrationBuilder.CreateIndex(
                name: "IX_kb_chunk_DocumentId_Ordinal",
                table: "kb_chunk",
                columns: new[] { "DocumentId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_kb_document_BaseId_Hash",
                table: "kb_document",
                columns: new[] { "BaseId", "Hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kb_document_State",
                table: "kb_document",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kb_chunk");

            migrationBuilder.DropTable(
                name: "kb_document");

            migrationBuilder.DropTable(
                name: "kb_base");
        }
    }
}
