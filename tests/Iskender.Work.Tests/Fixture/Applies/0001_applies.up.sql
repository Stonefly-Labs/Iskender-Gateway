-- The work fixture's own module (ISK-0009 T011): one row per apply a test handler commits, written through the handler's
-- unit, so a test can count applies per item. Scope-keyed: persistence contract Isolation rule 8.
CREATE TABLE applies (
    scope_key text NOT NULL,
    item_id uuid NOT NULL,
    attempt integer NOT NULL,
    PRIMARY KEY (scope_key, item_id, attempt)
);

ALTER TABLE applies ENABLE ROW LEVEL SECURITY;
ALTER TABLE applies FORCE ROW LEVEL SECURITY;

CREATE POLICY applies_scope ON applies FOR ALL
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
