using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CatalogSearch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CatalogWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "SourceDocumentId",
                table: "OperationLogs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "OperationLogs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Target",
                table: "OperationLogs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "InternalSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InternalProductRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Hash = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternalSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InternalSnapshots_InternalProductRecords_InternalProductRec~",
                        column: x => x.InternalProductRecordId,
                        principalTable: "InternalProductRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReviewActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    BeforeProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    AfterProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WatchFileStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileKey = table.Column<string>(type: "text", nullable: false),
                    FileHash = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchFileStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InternalSnapshots_InternalProductRecordId_Hash",
                table: "InternalSnapshots",
                columns: new[] { "InternalProductRecordId", "Hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewActions_EntityType_RecordId",
                table: "ReviewActions",
                columns: new[] { "EntityType", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_WatchFileStates_FileKey_FileHash",
                table: "WatchFileStates",
                columns: new[] { "FileKey", "FileHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InternalSnapshots");

            migrationBuilder.DropTable(
                name: "ReviewActions");

            migrationBuilder.DropTable(
                name: "WatchFileStates");

            migrationBuilder.DropColumn(
                name: "Details",
                table: "OperationLogs");

            migrationBuilder.DropColumn(
                name: "Target",
                table: "OperationLogs");

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceDocumentId",
                table: "OperationLogs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
