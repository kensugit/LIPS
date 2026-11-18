-- Reserved internal identity only: no business products, source imports or price/inventory adoption.
BEGIN;
CREATE TABLE IF NOT EXISTS "ExactSourcePairLinks" (
  "PairKey" text PRIMARY KEY,
  "ReservedProductId" uuid NOT NULL UNIQUE,
  "Version" integer NOT NULL CHECK ("Version" = 1),
  "RequestHash" text NOT NULL,
  "Body" jsonb NOT NULL,
  "CreatedAt" timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS "ExactSourcePairOperations" (
  "OperationId" uuid PRIMARY KEY,
  "PairKey" text NOT NULL REFERENCES "ExactSourcePairLinks"("PairKey"),
  "RequestHash" text NOT NULL,
  "Body" jsonb NOT NULL,
  "CreatedAt" timestamptz NOT NULL DEFAULT now()
);
COMMIT;
