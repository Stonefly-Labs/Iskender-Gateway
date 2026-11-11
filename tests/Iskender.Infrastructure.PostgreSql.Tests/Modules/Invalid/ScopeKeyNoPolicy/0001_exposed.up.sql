-- A scope-keyed table with no row-level security or policy.
CREATE TABLE exposed (
    scope_key text NOT NULL,
    name text NOT NULL
);
