CREATE EXTENSION IF NOT EXISTS vector;
CREATE TABLE IF NOT EXISTS coasters (
 id uuid PRIMARY KEY, title text NOT NULL, brewery text NOT NULL DEFAULT '',
 country text NOT NULL DEFAULT '', shape text NOT NULL DEFAULT '',
 dimensions text NOT NULL DEFAULT '', notes text NOT NULL DEFAULT '',
 front_path text NOT NULL, back_path text NOT NULL,
 front_vector vector(384) NOT NULL, back_vector vector(384) NOT NULL,
 front_blank boolean NOT NULL, back_blank boolean NOT NULL,
 model_version text NOT NULL, created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS copies (
 id uuid PRIMARY KEY, coaster_id uuid NOT NULL REFERENCES coasters(id) ON DELETE CASCADE,
 binder text NOT NULL DEFAULT '', page text NOT NULL DEFAULT '', position text NOT NULL DEFAULT '',
 condition text NOT NULL DEFAULT '', acquired_on date,
 created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS copies_coaster ON copies(coaster_id);
CREATE TABLE IF NOT EXISTS scans (
 id uuid PRIMARY KEY, front_path text NOT NULL, back_path text NOT NULL,
 front_vector vector(384) NOT NULL, back_vector vector(384) NOT NULL,
 front_blank boolean NOT NULL, back_blank boolean NOT NULL, model_version text NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now()
);
-- Exact search is intentional at ~1,000 items: both faces and both assignments are scored.
-- schema.sql is idempotent initialization, not a general migration framework.
