-- A scope-keyed table (persistence contract Isolation rule 8), usable by every scope.
CREATE TABLE slots (
    scope_key text NOT NULL,
    name text NOT NULL,
    value bigint NOT NULL,
    PRIMARY KEY (scope_key, name)
);

ALTER TABLE slots ENABLE ROW LEVEL SECURITY;
ALTER TABLE slots FORCE ROW LEVEL SECURITY;

CREATE POLICY slots_scope ON slots FOR ALL
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
