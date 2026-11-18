-- Read-only catalog metadata only; no business rows or stored source/answer JSON.
-- Run with psql -X -w -At -v ON_ERROR_STOP=1 -v schema=public -f check-intake-schema.sql.
BEGIN READ ONLY;
SET LOCAL search_path = pg_catalog;
SET LOCAL statement_timeout = '15s';
SELECT json_build_object('database',current_database(),'schema',:'schema','transactionReadOnly',current_setting('transaction_read_only'),'tables',COALESCE((
 SELECT json_agg(json_build_object(
  'name',c.relname,'kind',c.relkind,'persistence',c.relpersistence,'rls',c.relrowsecurity,'forceRls',c.relforcerowsecurity,
  'inherits',(SELECT count(*) FROM pg_inherits h WHERE h.inhrelid=c.oid OR h.inhparent=c.oid),
  'userTriggers',(SELECT count(*) FROM pg_trigger t WHERE t.tgrelid=c.oid AND NOT t.tgisinternal),
  'columns',(SELECT json_agg(json_build_object('name',a.attname,'type',format_type(a.atttypid,a.atttypmod),'notNull',a.attnotnull,'default',pg_get_expr(d.adbin,d.adrelid),'identity',a.attidentity,'generated',a.attgenerated) ORDER BY a.attnum)
    FROM pg_attribute a LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum WHERE a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped),
  'constraints',(SELECT COALESCE(json_agg(json_build_object('definition',pg_get_constraintdef(k.oid,false),'validated',k.convalidated,'deferrable',k.condeferrable,'deferred',k.condeferred) ORDER BY pg_get_constraintdef(k.oid,false)),'[]'::json) FROM pg_constraint k WHERE k.conrelid=c.oid),
  'indexes',(SELECT COALESCE(json_agg(json_build_object('method',am.amname,'unique',i.indisunique,'primary',i.indisprimary,'valid',i.indisvalid,'ready',i.indisready,'live',i.indislive,'immediate',i.indimmediate,'exclusion',i.indisexclusion,'nullsNotDistinct',i.indnullsnotdistinct,'keyCount',i.indnkeyatts,'columns',(SELECT json_agg(pg_get_indexdef(i.indexrelid,n,false) ORDER BY n) FROM generate_series(1,i.indnatts) n),'predicate',pg_get_expr(i.indpred,i.indrelid),'expressions',pg_get_expr(i.indexprs,i.indrelid))) ,'[]'::json)
   FROM pg_index i JOIN pg_class ic ON ic.oid=i.indexrelid JOIN pg_am am ON am.oid=ic.relam WHERE i.indrelid=c.oid)) ORDER BY c.relname)
 FROM pg_class c JOIN pg_namespace ns ON ns.oid=c.relnamespace
 WHERE ns.nspname=:'schema' AND c.relname IN ('IntakeReviews','IntakeReusableRuleVersions')),'[]'::json));
ROLLBACK;
