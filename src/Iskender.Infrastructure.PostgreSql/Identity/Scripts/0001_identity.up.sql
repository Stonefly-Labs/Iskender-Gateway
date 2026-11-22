-- The identity module, version 1 (ISK-0010 data model "Database objects", research R3, R4, and R11).
-- Every table is an operator table: persistence contract Isolation rule 6, used only in operator-scope units.

-- The installation: no row is uninitialized, one row is initialized. Only the bootstrap redemption inserts it.
CREATE TABLE installation (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    state text NOT NULL CHECK (state = 'initialized'),
    initialized_at timestamptz NOT NULL,
    initialized_by text NOT NULL
);

-- Forward-only: the runtime login may insert and read, never move the state back (persistence contract Isolation rule 11).
REVOKE UPDATE, DELETE, TRUNCATE ON installation FROM iskender_runtime;

-- Operator principals: an opaque ID, and no identity-provider subject, name, or email (FR-013).
CREATE TABLE principals (
    principal_id text PRIMARY KEY,
    kind text NOT NULL CHECK (kind = 'operator'),
    created_at timestamptz NOT NULL,
    created_through text NOT NULL CHECK (created_through IN ('bootstrap', 'recovery'))
);

-- Operator-scope grants. Roles are mapped to actions in code.
CREATE TABLE operator_grants (
    principal_id text NOT NULL REFERENCES principals,
    role text NOT NULL CHECK (role = 'system_operator'),
    granted_at timestamptz NOT NULL,
    PRIMARY KEY (principal_id, role)
);

-- Bootstrap, recovery, and enrollment tokens: the hash only, never the secret (research R4).
CREATE TABLE tokens (
    token_id text PRIMARY KEY CHECK (char_length(token_id) = 26),
    purpose text NOT NULL CHECK (purpose IN ('bootstrap', 'recovery', 'enrollment')),
    secret_hash bytea NOT NULL CHECK (octet_length(secret_hash) = 32),
    principal_id text REFERENCES principals CHECK ((principal_id IS NOT NULL) = (purpose = 'enrollment')),
    issued_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    consumed_at timestamptz,
    superseded_at timestamptz,
    CHECK (expires_at > issued_at),
    CHECK (consumed_at IS NULL OR superseded_at IS NULL)
);

-- At most one active bootstrap or recovery token per purpose.
CREATE UNIQUE INDEX tokens_active_purpose ON tokens (purpose)
    WHERE purpose IN ('bootstrap', 'recovery') AND consumed_at IS NULL AND superseded_at IS NULL;

-- At most one active enrollment code per principal.
CREATE UNIQUE INDEX tokens_active_enrollment ON tokens (principal_id)
    WHERE purpose = 'enrollment' AND consumed_at IS NULL AND superseded_at IS NULL;
