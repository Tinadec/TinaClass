using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinaClass.Api.Features.Chat.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_conversation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SpaceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FolderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TitleSetByUser = table.Column<bool>(type: "INTEGER", nullable: false),
                    Pinned = table.Column<bool>(type: "INTEGER", nullable: false),
                    Archived = table.Column<bool>(type: "INTEGER", nullable: false),
                    ModelCode = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    ParamsJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ActiveLeafId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TokenTotal = table.Column<long>(type: "INTEGER", nullable: false),
                    CostTotal = table.Column<long>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_conversation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "chat_folder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Sort = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_folder", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "chat_share",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", maxLength: 400000, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    RevokedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_share", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "chat_space",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Instruction = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_space", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "chat_message",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ContentJson = table.Column<string>(type: "TEXT", maxLength: 200000, nullable: false),
                    SearchText = table.Column<string>(type: "TEXT", maxLength: 200000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ModelCode = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    UsageJson = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Feedback = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_message", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chat_message_chat_conversation_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "chat_conversation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_citation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChunkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: false),
                    Quote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    DocumentName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    Where = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_citation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chat_citation_chat_message_MessageId",
                        column: x => x.MessageId,
                        principalTable: "chat_message",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_citation_MessageId_Position",
                table: "chat_citation",
                columns: new[] { "MessageId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_conversation_FolderId",
                table: "chat_conversation",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_chat_conversation_OwnerId_UpdatedAt",
                table: "chat_conversation",
                columns: new[] { "OwnerId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_folder_OwnerId_ParentId",
                table: "chat_folder",
                columns: new[] { "OwnerId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_message_ConversationId_ParentId",
                table: "chat_message",
                columns: new[] { "ConversationId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_share_ConversationId",
                table: "chat_share",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_chat_share_TokenHash",
                table: "chat_share",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chat_space_OwnerId",
                table: "chat_space",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_citation");

            migrationBuilder.DropTable(
                name: "chat_folder");

            migrationBuilder.DropTable(
                name: "chat_share");

            migrationBuilder.DropTable(
                name: "chat_space");

            migrationBuilder.DropTable(
                name: "chat_message");

            migrationBuilder.DropTable(
                name: "chat_conversation");
        }
    }
}
