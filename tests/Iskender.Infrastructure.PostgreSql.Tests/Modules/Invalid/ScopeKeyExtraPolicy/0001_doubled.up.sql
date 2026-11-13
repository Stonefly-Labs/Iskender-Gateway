-- A scope-keyed table with the scope-keyed policy and a second permissive policy that widens it.
CREATE TABLE doubled (
    scope_key text NOT NULL,
    name text NOT NULL
);

ALTER TABLE doubled ENABLE ROW LEVEL SECURITY;
ALTER TABLE doubled FORCE ROW LEVEL SECURITY;

CREATE POLICY doubled_scope ON doubled FOR ALL
    USING (
        scope_key = CASE nullif(current_setting('iskender.scope_kind', true), '')
          WHEN 'operator' THEN 'operator'
          WHEN 'organization' THEN 'org:' || nullif(current_setting('iskender.organization_id', true), '')
          WHEN 'project' THEN 'project:' || nullif(current_setting('iskender.organization_id', true), '')
                               || '/' || nullif(current_setting('iskender.project_id', true), '')
        END
    );

CREATE POLICY doubled_open ON doubled FOR SELECT USING (true);
