-- A tenant table with no row-level security or policy.
CREATE TABLE exposed (
    id text PRIMARY KEY,
    owner_organization_id text NOT NULL
);
