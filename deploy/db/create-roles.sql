-- One-time setup of the database and its roles (docs/database-design.md §5).
-- Run as a PostgreSQL superuser with psql, passing passwords as variables (never hard-code them):
--
--   psql -h <host> -U postgres -v migrator_password="..." -v app_password="..." -f deploy/db/create-roles.sql
--
-- tma_migrator owns the schemas and runs migrations. tma_app is what the application connects as:
-- it can read and write data but cannot change the schema. Audit-table restrictions (INSERT/SELECT only)
-- are applied by the migration that creates the audit table.

\set ON_ERROR_STOP on

SELECT format('CREATE ROLE tma_migrator LOGIN PASSWORD %L', :'migrator_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'tma_migrator') \gexec

SELECT format('CREATE ROLE tma_app LOGIN PASSWORD %L', :'app_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'tma_app') \gexec

SELECT 'CREATE DATABASE tma OWNER tma_migrator ENCODING ''UTF8'''
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'tma') \gexec

\connect tma

REVOKE ALL ON DATABASE tma FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE tma TO tma_app;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

-- Every table, sequence and schema the migrator creates is automatically usable (but not alterable) by the app.
ALTER DEFAULT PRIVILEGES FOR ROLE tma_migrator GRANT USAGE ON SCHEMAS TO tma_app;
ALTER DEFAULT PRIVILEGES FOR ROLE tma_migrator GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO tma_app;
ALTER DEFAULT PRIVILEGES FOR ROLE tma_migrator GRANT USAGE, SELECT ON SEQUENCES TO tma_app;
