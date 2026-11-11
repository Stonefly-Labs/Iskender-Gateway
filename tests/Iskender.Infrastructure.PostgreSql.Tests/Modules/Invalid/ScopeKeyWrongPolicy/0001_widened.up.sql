-- A scope-keyed table whose policy lets a project see its organization's rows: not the scope-keyed expression.
CREATE TABLE widened (
    scope_key text NOT NULL,
    name text NOT NULL
);

ALTER TABLE widened ENABLE ROW LEVEL SECURITY;
ALTER TABLE widened FORCE ROW LEVEL SECURITY;

CREATE POLICY widened_scope ON widened FOR ALL
    USING (scope_key IN ('org:' || nullif(current_setting('iskender.organization_id', true), ''), 'operator'));
