-- Audit log retention (FR-AUD-06, docs/database-design.md §7). Audit rows are append-only for the application: the
-- tma_app role has no DELETE right and a trigger blocks UPDATE/DELETE for every role. Removing old rows is therefore a
-- deliberate maintenance step run by the database owner, never by the app.
--
--   psql -h <host> -U tma_migrator -d tma -v retention_days=365 -f deploy/db/purge-audit-logs.sql
--
-- retention_days must be at least 365 (requirement: keep at least one year). The trigger is disabled only inside this
-- transaction, and the purge itself is recorded as an audit entry.

\set ON_ERROR_STOP on

SELECT :retention_days::int >= 365 AS retention_ok \gset
\if :retention_ok
\else
  \echo 'retention_days must be at least 365 (keep audit logs for at least one year). Nothing was deleted.'
  \quit
\endif

BEGIN;

ALTER TABLE audit.audit_logs DISABLE TRIGGER trg_audit_logs_append_only;

WITH purged AS (
    DELETE FROM audit.audit_logs
    WHERE occurred_at < now() - make_interval(days => :retention_days::int)
    RETURNING 1
)
INSERT INTO audit.audit_logs (occurred_at, actor_id, actor_ip, action, entity_type, entity_id, project_id, changes, correlation_id)
SELECT now(), NULL, NULL, 'audit.purged', 'AuditLog', 'retention', NULL,
       jsonb_build_object('retentionDays', :retention_days::int, 'deletedRows', (SELECT count(*) FROM purged)), NULL;

ALTER TABLE audit.audit_logs ENABLE TRIGGER trg_audit_logs_append_only;

COMMIT;
