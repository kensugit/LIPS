using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace CatalogSearch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalogPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalName = table.Column<string>(type: "text", nullable: false),
                    ProducerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: false),
                    CountryCode = table.Column<string>(type: "text", nullable: true),
                    Region = table.Column<string>(type: "text", nullable: true),
                    Vintage = table.Column<int>(type: "integer", nullable: true),
                    VolumeMl = table.Column<int>(type: "integer", nullable: true),
                    AttributesJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductSearchDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<int>(type: "integer", nullable: false),
                    SourceRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    SearchText = table.Column<string>(type: "text", nullable: false),
                    StructuredDataJson = table.Column<string>(type: "jsonb", nullable: false),
                    EmbeddingProvider = table.Column<string>(type: "text", nullable: true),
                    EmbeddingModel = table.Column<string>(type: "text", nullable: true),
                    IndexVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', coalesce(\"SearchText\", ''))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSearchDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SourceSystems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceSystems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InternalProductRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "text", nullable: false),
                    ProductName = table.Column<string>(type: "text", nullable: false),
                    ProductNameKana = table.Column<string>(type: "text", nullable: true),
                    JanCode = table.Column<string>(type: "text", nullable: true),
                    SupplierCode = table.Column<string>(type: "text", nullable: true),
                    VolumeMl = table.Column<int>(type: "integer", nullable: true),
                    CategoryCode = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourcePayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    RecordHash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternalProductRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InternalProductRecords_SourceSystems_SourceSystemId",
                        column: x => x.SourceSystemId,
                        principalTable: "SourceSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourceDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    FileHash = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceDocuments_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InternalProductLinks",
                columns: table => new
                {
                    InternalProductRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric", nullable: true),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    ReviewedBy = table.Column<string>(type: "text", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternalProductLinks", x => x.InternalProductRecordId);
                    table.ForeignKey(
                        name: "FK_InternalProductLinks_InternalProductRecords_InternalProduct~",
                        column: x => x.InternalProductRecordId,
                        principalTable: "InternalProductRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InternalProductLinks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExtractionJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParserVersion = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RowCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractionJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExtractionJobs_SourceDocuments_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "SourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventorySnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventorySnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventorySnapshots_SourceDocuments_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "SourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RowCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperationLogs_SourceDocuments_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "SourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierProductCode = table.Column<string>(type: "text", nullable: false),
                    SupplierProductName = table.Column<string>(type: "text", nullable: false),
                    ProductNameEn = table.Column<string>(type: "text", nullable: false),
                    ProducerNameJa = table.Column<string>(type: "text", nullable: false),
                    ProducerNameEn = table.Column<string>(type: "text", nullable: false),
                    Country = table.Column<string>(type: "text", nullable: false),
                    Region = table.Column<string>(type: "text", nullable: false),
                    ProductType = table.Column<string>(type: "text", nullable: false),
                    Vintage = table.Column<int>(type: "integer", nullable: true),
                    VintageRaw = table.Column<string>(type: "text", nullable: false),
                    VolumeMl = table.Column<int>(type: "integer", nullable: false),
                    TaxIncluded = table.Column<bool>(type: "boolean", nullable: false),
                    CurrentSourceDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    CurrentInventoryQuantity = table.Column<int>(type: "integer", nullable: true),
                    CurrentInventoryStatus = table.Column<int>(type: "integer", nullable: false),
                    CurrentObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierProducts_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierProducts_SourceDocuments_CurrentSourceDocumentId",
                        column: x => x.CurrentSourceDocumentId,
                        principalTable: "SourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierProducts_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InventorySnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RawValue = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryItems_InventorySnapshots_InventorySnapshotId",
                        column: x => x.InventorySnapshotId,
                        principalTable: "InventorySnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryItems_SupplierProducts_SupplierProductId",
                        column: x => x.SupplierProductId,
                        principalTable: "SupplierProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PriceHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: true),
                    RawValue = table.Column<string>(type: "text", nullable: false),
                    PriceType = table.Column<string>(type: "text", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    TaxIncluded = table.Column<bool>(type: "boolean", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceHistories_SourceDocuments_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "SourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PriceHistories_SupplierProducts_SupplierProductId",
                        column: x => x.SupplierProductId,
                        principalTable: "SupplierProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReviewTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewTasks_SupplierProducts_SupplierProductId",
                        column: x => x.SupplierProductId,
                        principalTable: "SupplierProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourceEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    SheetName = table.Column<string>(type: "text", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    RawCellsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExtractedRowJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceEvidence_SourceDocuments_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "SourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceEvidence_SupplierProducts_SupplierProductId",
                        column: x => x.SupplierProductId,
                        principalTable: "SupplierProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionJobs_SourceDocumentId",
                table: "ExtractionJobs",
                column: "SourceDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InternalProductLinks_ProductId",
                table: "InternalProductLinks",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalProductRecords_SourceSystemId_SourceKey",
                table: "InternalProductRecords",
                columns: new[] { "SourceSystemId", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_InventorySnapshotId_SupplierProductId",
                table: "InventoryItems",
                columns: new[] { "InventorySnapshotId", "SupplierProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_SupplierProductId",
                table: "InventoryItems",
                column: "SupplierProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InventorySnapshots_SourceDocumentId",
                table: "InventorySnapshots",
                column: "SourceDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationLogs_SourceDocumentId",
                table: "OperationLogs",
                column: "SourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceHistories_SourceDocumentId_SupplierProductId",
                table: "PriceHistories",
                columns: new[] { "SourceDocumentId", "SupplierProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceHistories_SupplierProductId",
                table: "PriceHistories",
                column: "SupplierProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSearchDocuments_EntityType_SourceRecordId",
                table: "ProductSearchDocuments",
                columns: new[] { "EntityType", "SourceRecordId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductSearchDocuments_SearchVector",
                table: "ProductSearchDocuments",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewTasks_SupplierProductId",
                table: "ReviewTasks",
                column: "SupplierProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceDocuments_SupplierId_FileHash",
                table: "SourceDocuments",
                columns: new[] { "SupplierId", "FileHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidence_SourceDocumentId_SupplierProductId",
                table: "SourceEvidence",
                columns: new[] { "SourceDocumentId", "SupplierProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidence_SupplierProductId",
                table: "SourceEvidence",
                column: "SupplierProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceSystems_Code",
                table: "SourceSystems",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierProducts_CurrentSourceDocumentId",
                table: "SupplierProducts",
                column: "CurrentSourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierProducts_ProductId",
                table: "SupplierProducts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierProducts_SupplierId_SupplierProductCode",
                table: "SupplierProducts",
                columns: new[] { "SupplierId", "SupplierProductCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Code",
                table: "Suppliers",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExtractionJobs");

            migrationBuilder.DropTable(
                name: "InternalProductLinks");

            migrationBuilder.DropTable(
                name: "InventoryItems");

            migrationBuilder.DropTable(
                name: "OperationLogs");

            migrationBuilder.DropTable(
                name: "PriceHistories");

            migrationBuilder.DropTable(
                name: "ProductSearchDocuments");

            migrationBuilder.DropTable(
                name: "ReviewTasks");

            migrationBuilder.DropTable(
                name: "SourceEvidence");

            migrationBuilder.DropTable(
                name: "InternalProductRecords");

            migrationBuilder.DropTable(
                name: "InventorySnapshots");

            migrationBuilder.DropTable(
                name: "SupplierProducts");

            migrationBuilder.DropTable(
                name: "SourceSystems");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "SourceDocuments");

            migrationBuilder.DropTable(
                name: "Suppliers");
        }
    }
}
