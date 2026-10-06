# Changelog

All notable changes to this solution are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The MSBuild `<Version>` for every `.csproj` is set once in [Directory.Build.props](Directory.Build.props).

## [Unreleased]

## [0.1.0] - 2026-10-06

System diagram: [docs/releases/v0.1.0.md](docs/releases/v0.1.0.md)

### Added

- Presigned MinIO PUT via `POST /videos/upload-url` (`videoId`, `presignedUrl`; Development also returns `uploadCommand`).
- `GET /videos/{id}` to read an upload's status; includes `videoUrl` (signed GET) once `done`.
- Upload page at `/upload`: pick a file and movement, watch upload progress, and follow a stepper through queued, analyzing, and results, with video playback at the end.
- Initial `uploads` row with status `awaiting_upload`; MinIO object-created webhook (`POST /webhooks/minio/object-created`) sets `pending` and publishes to Kafka.
- MinIO (`movement-storage`) with bucket `videos`; Kafka 4.3 (KRaft, `movement-kafka`) with topic `pose-estimation-jobs`.
- Python worker (`movement-worker`) that consumes jobs, stubs `process_video()`, and writes `processing` / `done` / `failed` to Postgres.
- `GET /health` also checks MinIO.

### Removed

- `POST /uploads` and `GET /uploads/{id}` (replaced by the `/videos` endpoints).

## [0.0.0] - 2026-09-18

System diagram: [docs/releases/v0.0.0.md](docs/releases/v0.0.0.md)

### Added

- ASP.NET Core 10 minimal API (`movement-analysis/api`, container `movement-api`) with `GET /health`, `POST /uploads` (dummy row), and `GET /uploads/{id}`.
- OpenAPI document at `/openapi/v1.json` and Swagger UI at `/swagger` (`/` redirects there).
- PostgreSQL 18 (`movement-database`) with `uploads` (spec columns, enums `movement_type` / `upload_status`).
- Docker Compose for both services; API can also run on the host against Postgres on `127.0.0.1:5433`.

[Unreleased]: #unreleased
[0.1.0]: docs/releases/v0.1.0.md
[0.0.0]: docs/releases/v0.0.0.md
