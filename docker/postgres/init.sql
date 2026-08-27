-- Postgres first-init script for the Isis stack. Runs once, from /docker-entrypoint-initdb.d, when the
-- data volume is empty. The ankane/pgvector image supports the standard init-dir convention.
--
-- The POSTGRES_DB env var already creates the "isis" database (Isis's metadata store). This script adds
-- the "recalldb" database (RecallDB's memory-content + vector store) on the same shared instance, and
-- enables the pgvector / pg_trgm extensions RecallDB needs there. Idempotent so re-runs are harmless.

-- Create the recalldb database if it does not already exist.
SELECT 'CREATE DATABASE recalldb'
    WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'recalldb')\gexec

-- Ensure the isis database exists too (belt-and-suspenders if POSTGRES_DB was changed).
SELECT 'CREATE DATABASE isis'
    WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'isis')\gexec

-- Enable the vector + trigram extensions inside the recalldb database (requires the pgvector binaries,
-- which the ankane/pgvector image ships).
\connect recalldb
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
