# ADR 003: Local Workspace Memory

Status: superseded

An early sample kept workspace state in local files so it could run without a database.
The full system replaced it: all state lives in Postgres behind the API, and the credential-free path is demo mode ([ADR 004](004-deterministic-fallback.md) and `DEMO_SNAPSHOT`).
The number stays reserved so later records keep theirs.
