-- A project-owned tenant table, and an operator table with no owner columns.
CREATE TABLE widget_notes (
    id text PRIMARY KEY,
    owner_organization_id text NOT NULL,
    owner_project_id text NOT NULL,
    widget_id text NOT NULL REFERENCES widgets (id),
    body text NOT NULL
);

ALTER TABLE widget_notes ENABLE ROW LEVEL SECURITY;
ALTER TABLE widget_notes FORCE ROW LEVEL SECURITY;

CREATE POLICY widget_notes_owner ON widget_notes
    USING (owner_organization_id = nullif(current_setting('iskender.organization_id', true), '') AND owner_project_id = nullif(current_setting('iskender.project_id', true), ''))
    WITH CHECK (owner_organization_id = nullif(current_setting('iskender.organization_id', true), '') AND owner_project_id = nullif(current_setting('iskender.project_id', true), ''));

CREATE TABLE settings (
    key text PRIMARY KEY,
    value text NOT NULL
);
