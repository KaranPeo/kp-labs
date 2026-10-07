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

1. **Request an upload URL** — client calls `POST /videos/upload-url` with `{ "fileName": "IMG_3141.mov" }` (optional `movementType`: `squat` (default) or `high_kick`).
   - A missing or blank `fileName`, or any other `movementType`, gets a 400. Directories are stripped from `fileName` (`../../etc/x.mov` becomes `x.mov`).
   - The API creates a `videoId` and the object key `{videoId}/{fileName}`.
   - It inserts an `uploads` row with status `awaiting_upload`.
   - It signs a PUT URL for that one key, valid for 15 minutes. Signing happens locally with the MinIO credentials; no call to MinIO.
   - Response: `videoId`, `presignedUrl`, and in Development a ready-to-run `uploadCommand` (curl).
2. **Upload the file** — client sends `PUT <presignedUrl>` with the file as the body, directly to MinIO on port 9000. MinIO checks the signature and expiry, then stores the object in the `videos` bucket. A changed signature, a different key, or a different verb (e.g. GET with the PUT URL) gets a 403.
3. **MinIO notifies the API** — on object creation MinIO POSTs an S3-style event to `/webhooks/minio/object-created` (configured by `MINIO_NOTIFY_WEBHOOK_*` and `movement-storage-init`). The API reads `Records[0].s3.object.key` and URL-decodes it (`chaos+test.mov` → `chaos test.mov`).
4. **Queue the job** — one statement moves the row from `awaiting_upload` to `pending`, matching on the object key: `UPDATE … WHERE video_object_key = @key AND status = 'awaiting_upload' RETURNING id, movement_type`.
   - If no row comes back, the event is acknowledged with 200 and ignored. That covers unknown keys, duplicate events, and re-uploads to the same URL (the row has already moved on). Malformed or empty bodies are ignored the same way.
   - Because the status check is in the `WHERE`, two racing deliveries can't both win.
   - The winner publishes `{ videoId, storageKey, exerciseType }` to Kafka topic `pose-estimation-jobs`, keyed by `videoId`.
5. **Worker picks it up** — `movement-worker` (consumer group `pose-estimation-worker`) reads the message and writes status straight to Postgres, opening a fresh connection per job:
   - Sets the row to `processing`, conditional on it still being `pending`. If that updates nothing (duplicate job), it skips.
   - Calls `process_video()`. In 0.1.0 that logs the job and sleeps 3 seconds; pose estimation comes later.
   - Sets `done` on success, or `failed` if `process_video()` raises.
   - Commits the Kafka offset only after the job is handled, so a crash mid-job means redelivery, not a lost job.
6. **Check status** — `GET /videos/{videoId}` returns the row: `awaiting_upload` until the PUT lands, then `pending`, `processing`, and `done` (or `failed`). Once `done` it also returns `videoUrl`, a signed GET URL valid for 15 minutes.

The upload page ([api/wwwroot/upload.html](api/wwwroot/upload.html)) runs steps 1, 2, and 6 in the browser: it PUTs the file with `XMLHttpRequest` for progress events, then polls `GET /videos/{videoId}` every second for up to 2 minutes. The browser upload is cross-origin (`localhost:8080` → `127.0.0.1:9000`), which works because MinIO allows any origin by default.

Stepper mapping: `awaiting_upload` → Queued (active, "waiting for storage"), `pending` → Queued (active, "waiting for a worker"), `processing` → Analyzing (active), `done` → Results (video player plus a download link), `failed` → Analyzing turns red. A failed presign or PUT turns that step red; a poll that never finishes turns the current step red after 2 minutes.

## When things go wrong

Each of these was tested against the full Compose stack for 0.1.0.

| Situation | What happens |
| --- | --- |
| Worker stopped | Jobs wait in Kafka; rows stay `pending` and the page stays on Queued. When the worker starts, it picks them up within about a second. |
| `docker stop movement-worker` | The worker handles SIGTERM, closes its Kafka consumer (leaving the group), and exits at once. Without this, Python as PID 1 ignores SIGTERM, Docker kills it after 10s, and the next worker waits ~45s for Kafka's session timeout before getting the partition. |
| API restarted mid-upload | The page keeps polling through the blip and finishes normally. |
| Postgres restarted | The API's first request afterwards can fail with a 500 (a pooled connection was killed; Npgsql discards it and the next request works). The worker is unaffected because it connects per job. |
| Postgres down when a job arrives | The worker can't connect, exits, and `restart: unless-stopped` restarts it with backoff. The offset isn't committed, so once Postgres is back the job is redelivered and finishes. |
| Malformed Kafka message (not JSON, no `videoId`) | Logged as `skip malformed job` and skipped; the worker keeps running. |
| Duplicate webhook, or a second PUT to the same signed URL | Ignored; no second job. The second PUT does overwrite the stored object while the URL is still valid (15 minutes). |
| Bad `movementType` / missing `fileName` | 400 with an error message. |

Known gaps:
- If the Kafka publish fails after the row is set to `pending`, no job is sent and the row stays `pending`.
- If the webhook call fails (e.g. the API is down or returns 500), the event can be lost: MinIO only queues and retries webhook events when `MINIO_NOTIFY_WEBHOOK_QUEUE_DIR` is set, and it isn't here. The row then stays `awaiting_upload` even though the file is stored.
- If the worker dies while a job is `processing`, the redelivered job is skipped (it's no longer `pending`), so the row stays `processing`.

## What each service is

- **movement-database** — Postgres 18, `uploads` table.
- **movement-storage** — MinIO. Init container creates bucket `videos` and the object-created webhook.
- **movement-kafka** — KRaft broker. Init container creates `pose-estimation-jobs`.
- **movement-api** — upload page, presigned URLs, webhook, Kafka produce.
- **movement-worker** — [worker/](worker/) Python consumer; writes `processing` / `done` / `failed` to Postgres. Restarts automatically if it exits.
