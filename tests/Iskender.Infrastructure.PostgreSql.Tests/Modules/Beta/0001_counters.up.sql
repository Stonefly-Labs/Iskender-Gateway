CREATE TABLE counters (
    id text PRIMARY KEY,
    owner_organization_id text NOT NULL,
    value bigint NOT NULL DEFAULT 0
);

ALTER TABLE counters ENABLE ROW LEVEL SECURITY;
ALTER TABLE counters FORCE ROW LEVEL SECURITY;

CREATE POLICY counters_owner ON counters
    USING (owner_organization_id = nullif(current_setting('iskender.organization_id', true), ''))
    WITH CHECK (owner_organization_id = nullif(current_setting('iskender.organization_id', true), ''));
