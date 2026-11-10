-- Records the order in which holders of an advisory lock ran.
CREATE TABLE lock_log (
    seq bigserial PRIMARY KEY,
    owner_organization_id text NOT NULL,
    holder text NOT NULL
);

ALTER TABLE lock_log ENABLE ROW LEVEL SECURITY;
ALTER TABLE lock_log FORCE ROW LEVEL SECURITY;

CREATE POLICY lock_log_owner ON lock_log
    USING (owner_organization_id = nullif(current_setting('iskender.organization_id', true), ''))
    WITH CHECK (owner_organization_id = nullif(current_setting('iskender.organization_id', true), ''));
