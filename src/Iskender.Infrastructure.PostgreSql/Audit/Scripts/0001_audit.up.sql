-- The audit module, version 1 (ISK-0010 data model "isk_audit.records", research R12).
-- An operator table: persistence contract Isolation rule 6, used only in operator-scope units.

-- Operator audit records: one row per audited security change, committed with the change it records.
CREATE TABLE records (
    audit_id text PRIMARY KEY,
    recorded_at timestamptz NOT NULL DEFAULT statement_timestamp(),
    actor text NOT NULL,
    target_principal text,
    action text NOT NULL CHECK (action IN (
        'bootstrap_token.issued', 'recovery_token.issued', 'token.superseded',
        'bootstrap.redeemed', 'recovery.redeemed', 'enrollment.consumed')),
    scope text NOT NULL CHECK (scope = 'operator'),
    result text NOT NULL CHECK (result IN ('succeeded', 'failed')),
    reason text NOT NULL CHECK (reason IN (
        'none', 'wrong_secret', 'wrong_purpose', 'consumed', 'superseded', 'expired',
        'already_initialized', 'not_initialized')),
    configuration_revision text NOT NULL,
    correlation_id text NOT NULL
);

-- Serves reads by time.
CREATE INDEX records_recorded_at ON records (recorded_at);

-- Append-only: the runtime login may insert and read, never edit or remove (persistence contract Isolation rule 11).
REVOKE UPDATE, DELETE, TRUNCATE ON records FROM iskender_runtime;
