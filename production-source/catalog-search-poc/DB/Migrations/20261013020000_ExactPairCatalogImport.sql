BEGIN;
ALTER TABLE "SupplierProducts" ADD COLUMN IF NOT EXISTS "CodeKind" text NOT NULL DEFAULT 'SupplierSku';
ALTER TABLE "SupplierProducts" ADD COLUMN IF NOT EXISTS "OriginalSupplierProductCode" text NULL;
CREATE UNIQUE INDEX IF NOT EXISTS "IX_InternalIdentityCode" ON "SupplierProducts"("SupplierProductCode") WHERE "CodeKind"='InternalIdentity';
CREATE TABLE IF NOT EXISTS "ExactSourcePairImports" (
 "PairKey" text PRIMARY KEY REFERENCES "ExactSourcePairLinks"("PairKey"),
 "SupplierProductId" uuid NOT NULL UNIQUE REFERENCES "SupplierProducts"("Id"),
 "RequestHash" text NOT NULL, "Body" jsonb NOT NULL
);
CREATE TABLE IF NOT EXISTS "ExactSourcePairImportOperations" (
 "OperationId" uuid PRIMARY KEY,
 "PairKey" text NOT NULL REFERENCES "ExactSourcePairImports"("PairKey"),
 "RequestHash" text NOT NULL, "Body" jsonb NOT NULL
);
COMMIT;
