-- Runs once when the Postgres data volume is first created
-- (docker-entrypoint-initdb.d). Recreating the volume is how you re-run this.

CREATE TYPE movement_type AS ENUM ('squat', 'high_kick');
CREATE TYPE upload_status AS ENUM (
  'awaiting_upload',
  'pending',
  'uploaded',
  'queued',
  'processing',
  'done',
  'failed'
);

CREATE TABLE uploads (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL,
  movement_type movement_type NOT NULL,
  video_object_key TEXT NOT NULL,
  status upload_status NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
