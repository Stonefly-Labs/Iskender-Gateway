-- The work module, version 1 (ISK-0009 data model "isk_work.items" and "isk_work.job_runs", research R5 and R10).
-- Every table is scope-keyed: persistence contract Isolation rule 8.

-- Work items: one row per enqueued item, read and written only in its own scope's unit.
CREATE TABLE items (
    scope_key text NOT NULL,
    item_id uuid NOT NULL,
    module text NOT NULL,
    kind text NOT NULL,
    payload_version integer NOT NULL CHECK (payload_version BETWEEN 1 AND 1000),
    payload bytea NOT NULL CHECK (octet_length(payload) <= 65536),
    idempotency_key text NOT NULL CHECK (char_length(idempotency_key) BETWEEN 1 AND 128),
    bucket smallint NOT NULL CHECK (bucket BETWEEN 0 AND 63),
    state text NOT NULL CHECK (state IN ('pending', 'completed', 'poisoned')),
    due_at timestamptz NOT NULL,
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    last_failure_class text CHECK (last_failure_class IN ('transient', 'permanent', 'unclassified', 'abandoned')),
    last_failure_code text,
    trace_parent text CHECK (char_length(trace_parent) <= 55),
    trace_state text CHECK (char_length(trace_state) <= 512),
    enqueued_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    finished_at timestamptz,
    PRIMARY KEY (scope_key, item_id),
    UNIQUE (scope_key, kind, idempotency_key)
);

-- Serves the shard counts and the claim.
CREATE INDEX items_due ON items (scope_key, bucket, due_at) WHERE state = 'pending';

-- Serves retention.
CREATE INDEX items_finished ON items (scope_key, state, finished_at) WHERE state <> 'pending';

ALTER TABLE items ENABLE ROW LEVEL SECURITY;
ALTER TABLE items FORCE ROW LEVEL SECURITY;

CREATE POLICY items_scope ON items FOR ALL
    USING (
        scope_key = CASE nullif(current_setting('iskender.scope_kind', true), '')
          WHEN 'operator' THEN 'operator'
          WHEN 'organization' THEN 'org:' || nullif(current_setting('iskender.organization_id', true), '')
          WHEN 'project' THEN 'project:' || nullif(current_setting('iskender.organization_id', true), '')
                               || '/' || nullif(current_setting('iskender.project_id', true), '')
        END
    )
    WITH CHECK (
        scope_key = CASE nullif(current_setting('iskender.scope_kind', true), '')
          WHEN 'operator' THEN 'operator'
          WHEN 'organization' THEN 'org:' || nullif(current_setting('iskender.organization_id', true), '')
          WHEN 'project' THEN 'project:' || nullif(current_setting('iskender.organization_id', true), '')
                               || '/' || nullif(current_setting('iskender.project_id', true), '')
        END
    );

-- Recurring job bookkeeping: one row per job, written only in the operator scope.
CREATE TABLE job_runs (
    scope_key text NOT NULL,
    job text NOT NULL,
    last_started_at timestamptz,
    last_finished_at timestamptz,
    cursor text CHECK (char_length(cursor) <= 512),
    PRIMARY KEY (scope_key, job)
);

ALTER TABLE job_runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE job_runs FORCE ROW LEVEL SECURITY;

CREATE POLICY job_runs_scope ON job_runs FOR ALL
    USING (
        scope_key = CASE nullif(current_setting('iskender.scope_kind', true), '')
          WHEN 'operator' THEN 'operator'
          WHEN 'organization' THEN 'org:' || nullif(current_setting('iskender.organization_id', true), '')
          WHEN 'project' THEN 'project:' || nullif(current_setting('iskender.organization_id', true), '')
                               || '/' || nullif(current_setting('iskender.project_id', true), '')
        END
    )
    WITH CHECK (
        scope_key = CASE nullif(current_setting('iskender.scope_kind', true), '')
          WHEN 'operator' THEN 'operator'
          WHEN 'organization' THEN 'org:' || nullif(current_setting('iskender.organization_id', true), '')
          WHEN 'project' THEN 'project:' || nullif(current_setting('iskender.organization_id', true), '')
                               || '/' || nullif(current_setting('iskender.project_id', true), '')
        END
    );
