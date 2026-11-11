-- The roles every test database uses (research R19). Run once per container by the superuser.
-- The runtime group: owns nothing, cannot bypass row-level security.
CREATE ROLE iskender_runtime NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
-- The runtime login: a member of the group, with no CREATE on any database.
CREATE ROLE isk_runtime_login LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS IN ROLE iskender_runtime;
-- The migration login: owns the module schemas it creates, so it needs CREATE on each test database.
CREATE ROLE isk_migrator LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
