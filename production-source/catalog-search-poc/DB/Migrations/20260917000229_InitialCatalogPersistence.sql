CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE TABLE "Products" (
        "Id" uuid NOT NULL,
        "CanonicalName" text NOT NULL,
        "ProducerId" uuid,
        "Category" text NOT NULL,
        "CountryCode" text,
        "Region" text,
        "Vintage" integer,
        "VolumeMl" integer,
        "AttributesJson" jsonb NOT NULL,
        CONSTRAINT "PK_Products" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE TABLE "ProductSearchDocuments" (
        "Id" uuid NOT NULL,
        "EntityType" integer NOT NULL,
        "SourceRecordId" uuid NOT NULL,
        "ProductId" uuid,
        "SearchText" text NOT NULL,
        "StructuredDataJson" jsonb NOT NULL,
        "EmbeddingProvider" text,
        "EmbeddingModel" text,
        "IndexVersion" integer NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "SearchVector" tsvector GENERATED ALWAYS AS (to_tsvector('simple', coalesce("SearchText", ''))) STORED,
        CONSTRAINT "PK_ProductSearchDocuments" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE TABLE "SourceSystems" (
        "Id" uuid NOT NULL,
        "Code" text NOT NULL,
        "Name" text NOT NULL,
        "Kind" integer NOT NULL,
        CONSTRAINT "PK_SourceSystems" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE TABLE "Suppliers" (
        "Id" uuid NOT NULL,
        "Code" text NOT NULL,
        "Name" text NOT NULL,
        CONSTRAINT "PK_Suppliers" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE TABLE "InternalProductRecords" (
        "Id" uuid NOT NULL,
        "SourceSystemId" uuid NOT NULL,
        "SourceKey" text NOT NULL,
        "ProductName" text NOT NULL,
        "ProductNameKana" text,
        "JanCode" text,
        "SupplierCode" text,
        "VolumeMl" integer,
        "CategoryCode" text,
        "Notes" text,
        "IsActive" boolean NOT NULL,
        "ObservedAt" timestamp with time zone NOT NULL,
        "SourcePayloadJson" jsonb NOT NULL,
        "RecordHash" text NOT NULL,
        CONSTRAINT "PK_InternalProductRecords" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_InternalProductRecords_SourceSystems_SourceSystemId" FOREIGN KEY ("SourceSystemId") REFERENCES "SourceSystems" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE TABLE "SourceDocuments" (
        "Id" uuid NOT NULL,
        "SupplierId" uuid NOT NULL,
        "FileName" text NOT NULL,
        "FileHash" text NOT NULL,
        "Content" bytea NOT NULL,
        "ObservedAt" timestamp with time zone NOT NULL,
        "ImportedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_SourceDocuments" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SourceDocuments_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE TABLE "InternalProductLinks" (
        "InternalProductRecordId" uuid NOT NULL,
        "ProductId" uuid,
        "Status" integer NOT NULL,
        "Confidence" numeric,
        "EvidenceJson" jsonb NOT NULL,
        "ReviewedBy" text,
        "ReviewedAt" timestamp with time zone,
        CONSTRAINT "PK_InternalProductLinks" PRIMARY KEY ("InternalProductRecordId"),
        CONSTRAINT "FK_InternalProductLinks_InternalProductRecords_InternalProduct~" FOREIGN KEY ("InternalProductRecordId") REFERENCES "InternalProductRecords" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_InternalProductLinks_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE TABLE "ExtractionJobs" (
        "Id" uuid NOT NULL,
        "SourceDocumentId" uuid NOT NULL,
        "ParserVersion" text NOT NULL,
        "Status" text NOT NULL,
        "RowCount" integer NOT NULL,
        CONSTRAINT "PK_ExtractionJobs" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ExtractionJobs_SourceDocuments_SourceDocumentId" FOREIGN KEY ("SourceDocumentId") REFERENCES "SourceDocuments" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE TABLE "InventorySnapshots" (
        "Id" uuid NOT NULL,
        "SourceDocumentId" uuid NOT NULL,
        "ObservedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_InventorySnapshots" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_InventorySnapshots_SourceDocuments_SourceDocumentId" FOREIGN KEY ("SourceDocumentId") REFERENCES "SourceDocuments" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE TABLE "OperationLogs" (
        "Id" uuid NOT NULL,
        "SourceDocumentId" uuid NOT NULL,
        "Action" text NOT NULL,
        "Status" text NOT NULL,
        "RowCount" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_OperationLogs" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_OperationLogs_SourceDocuments_SourceDocumentId" FOREIGN KEY ("SourceDocumentId") REFERENCES "SourceDocuments" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE TABLE "SupplierProducts" (
        "Id" uuid NOT NULL,
        "SupplierId" uuid NOT NULL,
        "ProductId" uuid,
        "SupplierProductCode" text NOT NULL,
        "SupplierProductName" text NOT NULL,
        "ProductNameEn" text NOT NULL,
        "ProducerNameJa" text NOT NULL,
        "ProducerNameEn" text NOT NULL,
        "Country" text NOT NULL,
        "Region" text NOT NULL,
        "ProductType" text NOT NULL,
        "Vintage" integer,
        "VintageRaw" text NOT NULL,
        "VolumeMl" integer NOT NULL,
        "TaxIncluded" boolean NOT NULL,
        "CurrentSourceDocumentId" uuid,
        "CurrentPrice" numeric,
        "CurrentInventoryQuantity" integer,
        "CurrentInventoryStatus" integer NOT NULL,
        "CurrentObservedAt" timestamp with time zone,
        CONSTRAINT "PK_SupplierProducts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SupplierProducts_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_SupplierProducts_SourceDocuments_CurrentSourceDocumentId" FOREIGN KEY ("CurrentSourceDocumentId") REFERENCES "SourceDocuments" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_SupplierProducts_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE TABLE "InventoryItems" (
        "Id" uuid NOT NULL,
        "InventorySnapshotId" uuid NOT NULL,
        "SupplierProductId" uuid NOT NULL,
        "Quantity" integer,
        "Status" integer NOT NULL,
        "RawValue" text NOT NULL,
        CONSTRAINT "PK_InventoryItems" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_InventoryItems_InventorySnapshots_InventorySnapshotId" FOREIGN KEY ("InventorySnapshotId") REFERENCES "InventorySnapshots" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_InventoryItems_SupplierProducts_SupplierProductId" FOREIGN KEY ("SupplierProductId") REFERENCES "SupplierProducts" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE TABLE "PriceHistories" (
        "Id" uuid NOT NULL,
        "SupplierProductId" uuid NOT NULL,
        "SourceDocumentId" uuid NOT NULL,
        "Amount" numeric,
        "RawValue" text NOT NULL,
        "PriceType" text NOT NULL,
        "Currency" text NOT NULL,
        "TaxIncluded" boolean NOT NULL,
        "ObservedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_PriceHistories" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_PriceHistories_SourceDocuments_SourceDocumentId" FOREIGN KEY ("SourceDocumentId") REFERENCES "SourceDocuments" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_PriceHistories_SupplierProducts_SupplierProductId" FOREIGN KEY ("SupplierProductId") REFERENCES "SupplierProducts" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE TABLE "ReviewTasks" (
        "Id" uuid NOT NULL,
        "SupplierProductId" uuid NOT NULL,
        "Status" text NOT NULL,
        "Reason" text NOT NULL,
        CONSTRAINT "PK_ReviewTasks" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ReviewTasks_SupplierProducts_SupplierProductId" FOREIGN KEY ("SupplierProductId") REFERENCES "SupplierProducts" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE TABLE "SourceEvidence" (
        "Id" uuid NOT NULL,
        "SourceDocumentId" uuid NOT NULL,
        "SupplierProductId" uuid NOT NULL,
        "SheetName" text NOT NULL,
        "RowNumber" integer NOT NULL,
        "RawCellsJson" jsonb NOT NULL,
        "ExtractedRowJson" jsonb NOT NULL,
        CONSTRAINT "PK_SourceEvidence" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SourceEvidence_SourceDocuments_SourceDocumentId" FOREIGN KEY ("SourceDocumentId") REFERENCES "SourceDocuments" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_SourceEvidence_SupplierProducts_SupplierProductId" FOREIGN KEY ("SupplierProductId") REFERENCES "SupplierProducts" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE UNIQUE INDEX "IX_ExtractionJobs_SourceDocumentId" ON "ExtractionJobs" ("SourceDocumentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE INDEX "IX_InternalProductLinks_ProductId" ON "InternalProductLinks" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE UNIQUE INDEX "IX_InternalProductRecords_SourceSystemId_SourceKey" ON "InternalProductRecords" ("SourceSystemId", "SourceKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE UNIQUE INDEX "IX_InventoryItems_InventorySnapshotId_SupplierProductId" ON "InventoryItems" ("InventorySnapshotId", "SupplierProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE INDEX "IX_InventoryItems_SupplierProductId" ON "InventoryItems" ("SupplierProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE UNIQUE INDEX "IX_InventorySnapshots_SourceDocumentId" ON "InventorySnapshots" ("SourceDocumentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE INDEX "IX_OperationLogs_SourceDocumentId" ON "OperationLogs" ("SourceDocumentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE UNIQUE INDEX "IX_PriceHistories_SourceDocumentId_SupplierProductId" ON "PriceHistories" ("SourceDocumentId", "SupplierProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE INDEX "IX_PriceHistories_SupplierProductId" ON "PriceHistories" ("SupplierProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE UNIQUE INDEX "IX_ProductSearchDocuments_EntityType_SourceRecordId" ON "ProductSearchDocuments" ("EntityType", "SourceRecordId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE INDEX "IX_ProductSearchDocuments_SearchVector" ON "ProductSearchDocuments" USING GIN ("SearchVector");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE UNIQUE INDEX "IX_ReviewTasks_SupplierProductId" ON "ReviewTasks" ("SupplierProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE UNIQUE INDEX "IX_SourceDocuments_SupplierId_FileHash" ON "SourceDocuments" ("SupplierId", "FileHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE UNIQUE INDEX "IX_SourceEvidence_SourceDocumentId_SupplierProductId" ON "SourceEvidence" ("SourceDocumentId", "SupplierProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE INDEX "IX_SourceEvidence_SupplierProductId" ON "SourceEvidence" ("SupplierProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE UNIQUE INDEX "IX_SourceSystems_Code" ON "SourceSystems" ("Code");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE INDEX "IX_SupplierProducts_CurrentSourceDocumentId" ON "SupplierProducts" ("CurrentSourceDocumentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE INDEX "IX_SupplierProducts_ProductId" ON "SupplierProducts" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE UNIQUE INDEX "IX_SupplierProducts_SupplierId_SupplierProductCode" ON "SupplierProducts" ("SupplierId", "SupplierProductCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    CREATE UNIQUE INDEX "IX_Suppliers_Code" ON "Suppliers" ("Code");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917000229_InitialCatalogPersistence') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260917000229_InitialCatalogPersistence', '10.0.4');
    END IF;
END $EF$;
COMMIT;
