# Changelog

All notable changes to this solution are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The MSBuild `<Version>` for every `.csproj` is set once in [Directory.Build.props](Directory.Build.props).

## [Unreleased]

### Added

### Changed

### Fixed

### Removed

## [0.0.0] - 2026-09-18

System diagram: [docs/releases/v0.0.0.md](docs/releases/v0.0.0.md)

### Added

- ASP.NET Core 10 minimal API (`movement-analysis/api`, container `movement-api`) with `GET /health`, `POST /uploads` (dummy row), and `GET /uploads/{id}`.
- OpenAPI document at `/openapi/v1.json` and Swagger UI at `/swagger` (`/` redirects there).
- PostgreSQL 18 (`movement-database`) with `uploads` (spec columns, enums `movement_type` / `upload_status`).
- Docker Compose for both services; API can also run on the host against Postgres on `127.0.0.1:5433`.

[Unreleased]: #unreleased
[0.0.0]: docs/releases/v0.0.0.md
