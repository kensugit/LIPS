-- Apply only to the explicitly selected isolated review DB. No business tables changed.
CREATE TABLE IF NOT EXISTS "IntakeReusableRuleVersions" (
    "RuleId" uuid NOT NULL,
    "Version" integer NOT NULL CHECK ("Version" > 0),
    "OperationId" uuid NOT NULL UNIQUE,
    "RequestHash" text NOT NULL CHECK (length("RequestHash") = 64),
    "ExpectedVersion" integer NOT NULL CHECK ("ExpectedVersion" >= 0),
    "Body" jsonb NOT NULL,
    "CreatedAt" timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY ("RuleId", "Version")
);
