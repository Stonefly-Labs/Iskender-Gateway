-- A scope directory table with no delete policy.

CREATE TABLE entries (
    listed_scope text NOT NULL,
    module text NOT NULL,
    due_at timestamptz,
    PRIMARY KEY (listed_scope, module)
);

ALTER TABLE entries ENABLE ROW LEVEL SECURITY;
ALTER TABLE entries FORCE ROW LEVEL SECURITY;

CREATE POLICY entries_read ON entries FOR SELECT
    USING (
        listed_scope = CASE nullif(current_setting('iskender.scope_kind', true), '')
          WHEN 'operator' THEN 'operator'
          WHEN 'organization' THEN 'org:' || nullif(current_setting('iskender.organization_id', true), '')
          WHEN 'project' THEN 'project:' || nullif(current_setting('iskender.organization_id', true), '')
                               || '/' || nullif(current_setting('iskender.project_id', true), '')
        END
        OR nullif(current_setting('iskender.scope_kind', true), '') = 'operator'
    );

CREATE POLICY entries_insert ON entries FOR INSERT
    WITH CHECK (
        listed_scope = CASE nullif(current_setting('iskender.scope_kind', true), '')
          WHEN 'operator' THEN 'operator'
          WHEN 'organization' THEN 'org:' || nullif(current_setting('iskender.organization_id', true), '')
          WHEN 'project' THEN 'project:' || nullif(current_setting('iskender.organization_id', true), '')
                               || '/' || nullif(current_setting('iskender.project_id', true), '')
        END
    );

CREATE POLICY entries_update ON entries FOR UPDATE
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
