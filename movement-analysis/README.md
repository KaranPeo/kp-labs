# movement-analysis — 0.1.0

Upload page, presigned video upload to MinIO, webhook → Kafka, Python worker stub that reports status.

## Run (Mac)

Copy [`.env.example`](../.env.example) to [`.env`](../.env) at the repo root. MinIO password must be at least 8 characters.

If this machine already ran 0.0.0, recreate the Postgres volume so `awaiting_upload` / `pending` exist (`docker compose --env-file ../.env down -v` wipes local DB and MinIO data).

F5 **Movement-Analysis (Docker)** starts everything. Swagger: [http://localhost:8080/swagger](http://localhost:8080/swagger). MinIO console: [http://localhost:9001](http://localhost:9001).

**Upload test (browser)**

Open [http://localhost:8080/upload](http://localhost:8080/upload), pick a video and a movement, and click Upload. The stepper fills as the upload moves through the system: Get upload link → Upload to storage (progress bar) → Queued for analysis → Analyzing (worker started) → Results (worker done, video plays).

iPhone `.mov` files (HEVC) play in Safari but may not in Chrome; the page shows a download link as a fallback.

**Manual upload test (curl)**

Export a clip from Photos into this folder (or copy one here). Use that exact file name in the request — `fileName` is the name of the file on disk, not the Photos library path.

1. F5 **Movement-Analysis (Docker)** (or start Compose yourself).
2. `POST /videos/upload-url` with `{ "fileName": "clip.mp4" }`.
3. In Development the response includes `uploadCommand`. Run it from this folder, next to the video:
   `cd movement-analysis` then paste the command.
4. MinIO notifies the API; status becomes `pending`; the worker picks up the Kafka job (`docker logs -f movement-worker`) and moves it to `processing`, then `done`.
5. `GET /videos/{videoId}` (includes `videoUrl` once `done`).

`.mov` files in this folder are gitignored so Photos exports stay local.

## Upload flow

The client never sends video bytes through the API. The API hands out a short-lived URL, the client uploads straight to MinIO, and MinIO tells the API when the file has landed. Diagram: [docs/releases/v0.1.0.md](../docs/releases/v0.1.0.md).

1. **Request an upload URL** — client calls `POST /videos/upload-url` with `{ "fileName": "IMG_3141.mov" }` (optional `movementType`, default `squat`).
   - The API creates a `videoId` and the object key `{videoId}/{fileName}`.
   - It inserts an `uploads` row with status `awaiting_upload`.
   - It signs a PUT URL for that one key, valid for 15 minutes. Signing happens locally with the MinIO credentials; no call to MinIO.
   - Response: `videoId`, `presignedUrl`, and in Development a ready-to-run `uploadCommand` (curl).
2. **Upload the file** — client sends `PUT <presignedUrl>` with the file as the body, directly to MinIO on port 9000. MinIO checks the signature and expiry, then stores the object in the `videos` bucket.
3. **MinIO notifies the API** — on object creation MinIO POSTs an S3-style event to `/webhooks/minio/object-created` (configured by `MINIO_NOTIFY_WEBHOOK_*` and `movement-storage-init`).
   - The API reads `Records[0].s3.object.key`, URL-decodes it, and looks up the `uploads` row with that key.
   - Unknown keys, malformed events, and rows that are no longer `awaiting_upload` (duplicate events) are acknowledged with 200 and ignored.
4. **Queue the job** — the API updates the row to `pending`, conditional on it still being `awaiting_upload`, so only one delivery wins. It then publishes `{ videoId, storageKey, exerciseType }` to Kafka topic `pose-estimation-jobs`, keyed by `videoId`.
5. **Worker picks it up** — `movement-worker` (consumer group `pose-estimation-worker`) reads the message and writes status straight to Postgres:
   - Sets the row to `processing`, conditional on it still being `pending`. If that updates nothing (duplicate job), it skips.
   - Calls `process_video()`. In 0.1.0 that logs the job and sleeps 3 seconds; pose estimation comes later.
   - Sets `done` on success, or `failed` if `process_video()` raises.
6. **Check status** — `GET /videos/{videoId}` returns the row: `awaiting_upload` until the PUT lands, then `pending`, `processing`, and `done` (or `failed`). Once `done` it also returns `videoUrl`, a signed GET URL valid for 15 minutes.

The upload page ([api/wwwroot/upload.html](api/wwwroot/upload.html)) runs steps 1, 2, and 6 in the browser: it PUTs the file with `XMLHttpRequest` for progress events, then polls `GET /videos/{videoId}` every second. The browser upload is cross-origin (`localhost:8080` → `127.0.0.1:9000`), which works because MinIO allows any origin by default.

Known gap: if the Kafka publish fails after the row is set to `pending`, no job is sent and the row stays `pending`.

## What each service is

- **movement-database** — Postgres 18, `uploads` table.
- **movement-storage** — MinIO. Init container creates bucket `videos` and the object-created webhook.
- **movement-kafka** — KRaft broker. Init container creates `pose-estimation-jobs`.
- **movement-api** — upload page, presigned URLs, webhook, Kafka produce.
- **movement-worker** — [worker/](worker/) Python consumer; writes `processing` / `done` / `failed` to Postgres.
