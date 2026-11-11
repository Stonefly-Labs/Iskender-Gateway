-- The coordination module, version 1 (ISK-0005 data model "Database objects", research R6 and R8).
-- Every table is scope-keyed: persistence contract Isolation rule 8.

-- Atomic operations: one value and its version per key.
CREATE TABLE coordination_values (
    scope_key text NOT NULL,
    key_hash text NOT NULL,
    value bigint NOT NULL,
    version bigint NOT NULL,
    expires_at timestamptz NOT NULL,
    PRIMARY KEY (scope_key, key_hash)
);

ALTER TABLE coordination_values ENABLE ROW LEVEL SECURITY;
ALTER TABLE coordination_values FORCE ROW LEVEL SECURITY;

CREATE POLICY coordination_values_scope ON coordination_values FOR ALL
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

-- Leases: the holder and fencing token of each lease key.
CREATE TABLE leases (
    scope_key text NOT NULL,
    key_hash text NOT NULL,
    holder uuid NOT NULL,
    token bigint NOT NULL,
    expires_at timestamptz NOT NULL,
    PRIMARY KEY (scope_key, key_hash)
);

ALTER TABLE leases ENABLE ROW LEVEL SECURITY;
ALTER TABLE leases FORCE ROW LEVEL SECURITY;

CREATE POLICY leases_scope ON leases FOR ALL
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

-- The fencing guard: the highest token accepted per resource, and the lease key it was first fenced with.
CREATE TABLE fences (
    scope_key text NOT NULL,
    resource_hash text NOT NULL,
    lease_hash text NOT NULL,
    highest_token bigint NOT NULL,
    PRIMARY KEY (scope_key, resource_hash)
);

ALTER TABLE fences ENABLE ROW LEVEL SECURITY;
ALTER TABLE fences FORCE ROW LEVEL SECURITY;

CREATE POLICY fences_scope ON fences FOR ALL
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

-- Cache invalidation: the generation of each namespace.
CREATE TABLE cache_generations (
    scope_key text NOT NULL,
    namespace_hash text NOT NULL,
    generation bigint NOT NULL,
    PRIMARY KEY (scope_key, namespace_hash)
);

ALTER TABLE cache_generations ENABLE ROW LEVEL SECURITY;
ALTER TABLE cache_generations FORCE ROW LEVEL SECURITY;

CREATE POLICY cache_generations_scope ON cache_generations FOR ALL
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

-- Fencing tokens: strictly increasing across all keys, never reused, and WAL-logged, so not lost across a restart.
CREATE SEQUENCE fencing_tokens AS bigint;

GRANT USAGE ON SEQUENCE fencing_tokens TO iskender_runtime;
