# movement-analysis — V0 (API + Postgres)

Prove the local loop: write a row, read it back. No MinIO, queue, workers, or auth yet.

## Run (Mac)

Docker Desktop running. C# extension installed (`ms-dotnettools.csharp`). Open the `kp-labs` workspace.

Two Run and Debug configs (dropdown next to the green play button):

**API + Postgres (Docker)** — default F5. Builds and starts both containers. Opens [http://localhost:8080/swagger](http://localhost:8080/swagger). No breakpoints (Release image). Stop the debug session does not stop the containers; use `docker compose stop` in `movement-analysis/` when you want them down.

**Debug API (host)** — Stops the API container (frees port 8080), keeps Postgres in Docker, runs the API on local allowing for debug. Host API uses **127.0.0.1:5433** so it does not hit Homebrew Postgres on 5432.

Same Swagger steps either way: **POST /uploads** → copy `id` → **GET /uploads/{id}**.

Do not run both configs at once — they share port 8080.

`init.sql` only runs when the Postgres volume is first created. To reset schema:

Copy [`.env.example`](../.env.example) to [`.env`](../.env) at the repo root (gitignored). Compose and the API read `POSTGRES_USER`, `POSTGRES_PASSWORD`, and `POSTGRES_DB` from there.

```bash
cd movement-analysis
docker compose --env-file ../.env down -v
docker compose --env-file ../.env up --build
```

## What each file is

- **`docker-compose.yml`** — two services on one Docker network. Service names and container names are both `movement-database` and `movement-api` (Compose DNS and `docker ps` match). Image is official `postgres:18` (current stable major, ARM64 on Apple Silicon). Pin the major (`18`), not `latest`. Credentials come from repo-root `.env` (`POSTGRES_USER` / `PASSWORD` / `DB`). Host port is **5433** → container 5432 so it does not collide with Homebrew Postgres on 5432. The named volume `postgres_data` mounts at `/var/lib/postgresql` (Postgres 18+ image layout). `./db/init.sql:/docker-entrypoint-initdb.d/init.sql:ro` bind-mounts our schema file into the official image’s first-boot folder; Postgres runs `*.sql` there **only when the data volume is empty**. `:ro` means the container cannot write back to the file on your Mac. `healthcheck` + `pg_isready` lets `movement-api` wait until the database accepts connections. `movement-api` is built from `api/Dockerfile` and connects to host `movement-database` on port 5432 *inside* the network.

- **`db/init.sql`** — Postgres `ENUM` types for `movement_type` and `upload_status`, then `uploads` with the spec columns.

- **`api/Dockerfile`** — multi-stage: `sdk:10.0` restores and publishes; `aspnet:10.0` is a smaller runtime image. `ASPNETCORE_URLS=http://0.0.0.0:8080` makes Kestrel listen on all interfaces inside the container (required for port mapping). Official Microsoft images publish `linux/arm64`.

- **`api/MovementAnalysis.Api.csproj`** — `net10.0` web SDK, Npgsql 10, Microsoft.AspNetCore.OpenApi, Swashbuckle Swagger UI. No ORM.

Repo-root **`.vscode/launch.json`** / **`tasks.json`**: **API + Postgres (Docker)** runs `docker compose up --build -d --wait`. **Debug API (host)** stops the API container, starts Postgres, then debugs the DLL on port 8080.

- **`api/appsettings.json`** — logging only. DB user/password/name come from repo-root `.env` (or the process environment). Host/port: Compose sets `POSTGRES_HOST=movement-database` and `POSTGRES_PORT=5432`; local debug defaults to `127.0.0.1:5433`.

- **`api/Program.cs`** — minimal APIs. `NpgsqlDataSource` is a singleton connection pool. `GET /health` runs `SELECT 1` so a 503 means the API process is up but Postgres is not. `POST /uploads` inserts dummy values for every column and returns `201` + the row. `GET /uploads/{id}` reads it back or `404`. SQL parameters (`@id`, …) plus `::movement_type` / `::upload_status` casts keep enum values typed without extra Npgsql enum mapping. `AddOpenApi` + Swagger UI at `/swagger` for clicking endpoints in a browser. `/` redirects there.

- **`api/.dockerignore`** — keeps local `bin/` / `obj/` out of the image build context.
