-- The directory module, version 1 (ISK-0009 research R2, data model "isk_directory.scopes").
-- A scope directory table: persistence contract Isolation rule 9. Every unit writes and locks only its own scope's rows,
-- and an operator unit also reads every row.

CREATE TABLE scopes (
    listed_scope text NOT NULL,
    module text NOT NULL CHECK (module ~ '^[a-z][a-z0-9_]{0,30}$'),
    due_at timestamptz,
    PRIMARY KEY (listed_scope, module)
);

-- Serves the dispatcher's and the jobs' keyset page over one module's due scopes.
CREATE INDEX scopes_due ON scopes (module, listed_scope) WHERE due_at IS NOT NULL;

ALTER TABLE scopes ENABLE ROW LEVEL SECURITY;
ALTER TABLE scopes FORCE ROW LEVEL SECURITY;

CREATE POLICY scopes_read ON scopes FOR SELECT
    USING (
        listed_scope = CASE nullif(current_setting('iskender.scope_kind', true), '')
          WHEN 'operator' THEN 'operator'
          WHEN 'organization' THEN 'org:' || nullif(current_setting('iskender.organization_id', true), '')
          WHEN 'project' THEN 'project:' || nullif(current_setting('iskender.organization_id', true), '')
                               || '/' || nullif(current_setting('iskender.project_id', true), '')
        END
        OR nullif(current_setting('iskender.scope_kind', true), '') = 'operator'
    );

CREATE POLICY scopes_insert ON scopes FOR INSERT
    WITH CHECK (
        listed_scope = CASE nullif(current_setting('iskender.scope_kind', true), '')
          WHEN 'operator' THEN 'operator'
          WHEN 'organization' THEN 'org:' || nullif(current_setting('iskender.organization_id', true), '')
          WHEN 'project' THEN 'project:' || nullif(current_setting('iskender.organization_id', true), '')
                               || '/' || nullif(current_setting('iskender.project_id', true), '')
        END
    );

CREATE POLICY scopes_update ON scopes FOR UPDATE
    USING (
        listed_scope = CASE nullif(current_setting('iskender.scope_kind', true), '')
          WHEN 'operator' THEN 'operator'
          WHEN 'organization' THEN 'org:' || nullif(current_setting('iskender.organization_id', true), '')
          WHEN 'project' THEN 'project:' || nullif(current_setting('iskender.organization_id', true), '')
                               || '/' || nullif(current_setting('iskender.project_id', true), '')
        END
    )
    WITH CHECK (
        listed_scope = CASE nullif(current_setting('iskender.scope_kind', true), '')
          WHEN 'operator' THEN 'operator'
          WHEN 'organization' THEN 'org:' || nullif(current_setting('iskender.organization_id', true), '')
          WHEN 'project' THEN 'project:' || nullif(current_setting('iskender.organization_id', true), '')
                               || '/' || nullif(current_setting('iskender.project_id', true), '')
        END
    );

CREATE POLICY scopes_delete ON scopes FOR DELETE
    USING (
        listed_scope = CASE nullif(current_setting('iskender.scope_kind', true), '')
          WHEN 'operator' THEN 'operator'
          WHEN 'organization' THEN 'org:' || nullif(current_setting('iskender.organization_id', true), '')
          WHEN 'project' THEN 'project:' || nullif(current_setting('iskender.organization_id', true), '')
                               || '/' || nullif(current_setting('iskender.project_id', true), '')
        END
    );
