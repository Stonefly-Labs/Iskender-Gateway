-- An organization-owned, versioned tenant table.
CREATE TABLE widgets (
    id text PRIMARY KEY,
    owner_organization_id text NOT NULL,
    name text NOT NULL,
    row_version bigint NOT NULL DEFAULT 1
);

ALTER TABLE widgets ENABLE ROW LEVEL SECURITY;
ALTER TABLE widgets FORCE ROW LEVEL SECURITY;

CREATE POLICY widgets_owner ON widgets
    USING (owner_organization_id = nullif(current_setting('iskender.organization_id', true), ''))
    WITH CHECK (owner_organization_id = nullif(current_setting('iskender.organization_id', true), ''));
