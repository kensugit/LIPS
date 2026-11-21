# Official importer acquisition entry (offline candidate)

This standalone CLI references Infrastructure; Web does not reference or invoke it.
No database access is performed. No live acquisition has been tested or authorized by this artifact.

Required arguments: `--request`, `--policy`, `--request-sha256`, `--policy-sha256`, `--store-root`, `--receipt-root`.
All paths must be absolute and all parent directories must already exist without reparse points.
Expected hashes must come from the reviewed handoff, not from an untrusted request itself.
Request and policy are bounded to 512 KiB and 32 KiB; JSON rejects unknown and duplicate properties.

Default mode (or `--validate-only`) verifies captured bytes, policy and supplied source fingerprint,
then reports `VALIDATED_NOT_EXECUTED`, writes nothing and makes no HTTP request.
This validates inputs only, not current cache generation, network reachability, official page structure,
deployment ownership, or successful acquisition.

`--fetch-and-save` creates an exclusive operation intent before calling acquisition once.
The reviewed fetcher may follow at most three redirects; there is no CLI retry.
Both intent and result are CreateNew. Any existing operation record blocks execution.
Failures preserve the intent and partial storage state; do not rerun, rename operation IDs to bypass
the guard, remove markers, or infer no change from a failed result. `STOPPED_OR_UNKNOWN` needs reconciliation.
Page contents, product names, raw exceptions, credentials and original source JSON are not printed.
Only input hashes, status, phase, allowed error type and storage record references are reported.

Request schema: `lips-importer-acquisition-request/v1`; exact fields are `schema`, `operationId`,
`identity`, `sourceDocumentId`, `extractedJson`, `originalFingerprint`, `officialUrl`, `expectedGeneration`.
Identity fields follow `ImporterProductIdentity` and require exact SKU, producer, product, vintage,
volume and variant. Source fingerprint uses `SourceFingerprint.Create` without rewriting original JSON.
The request producer must read `ExtractedRowJson` from the current persisted database row for
the exact product/source pair, then retain that returned string verbatim. PostgreSQL jsonb may
change whitespace, key order and Unicode representation on storage. Do not use pre-insert JSON
or reserialize the returned JSON to calculate the fingerprint. The CLI checks internal consistency
of supplied inputs; it does not prove their equality to the current database. A reviewed database
readback/export step is still required before production acquisition. Search independently checks
the persisted current fingerprint and withholds stale or differently serialized evidence.
Policy schema: `lips-importer-acquisition-policy/v1`; exact fields are `schema`, `rules` (supplierCode/host),
`maximumFiles`, `maximumTotalBytes`. The exact host list still requires deployment review.

Receipt files use Flush(true), readback, and Linux directory fsync. Windows lacks the same directory
durability guarantee. Linux syscall paths have not been exercised on a Linux host in this work.
Protected parent directories and a dedicated writer identity remain deployment prerequisites;
metadata checks are not a replacement for OS ownership controls against concurrent hostile path changes.

Validation: local-cache-only restore/build; 19 real net10 xUnit tests, synthetic inputs and validate-only
filesystem checks. Fetch-mode control flow is tested with a fake acquisition delegate only. No real fetch invocation, external HTTP, production operation, model or database tests.
