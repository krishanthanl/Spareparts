-- Local development initialization only; executed once for a new volume.
\getenv app_password APP_DB_PASSWORD
\getenv migration_password MIGRATION_DB_PASSWORD
CREATE ROLE spareparts_app LOGIN PASSWORD :'app_password';
CREATE ROLE spareparts_migrator LOGIN PASSWORD :'migration_password';
REVOKE ALL ON DATABASE spareparts FROM PUBLIC;
GRANT CONNECT ON DATABASE spareparts TO spareparts_app, spareparts_migrator;
GRANT CREATE ON DATABASE spareparts TO spareparts_migrator;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
ALTER SCHEMA public OWNER TO spareparts_migrator;
GRANT USAGE ON SCHEMA public TO spareparts_app;
ALTER DEFAULT PRIVILEGES FOR ROLE spareparts_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO spareparts_app;
ALTER DEFAULT PRIVILEGES FOR ROLE spareparts_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO spareparts_app;
-- Phase 3 must narrow ledger grants/add immutable-row protection explicitly.
