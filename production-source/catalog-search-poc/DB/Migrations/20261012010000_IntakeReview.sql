-- Apply explicitly to the isolated review database. Never executed by Web startup.
CREATE TABLE IF NOT EXISTS "IntakeReviews" (
 "Id" uuid PRIMARY KEY,
 "Hash" text NOT NULL,
 "Path" text NOT NULL,
 "Content" bytea NOT NULL,
 "Version" integer NOT NULL,
 "Body" jsonb NOT NULL,
 "UpdatedAt" timestamptz NOT NULL DEFAULT now(),
 UNIQUE ("Hash", "Path")
);
