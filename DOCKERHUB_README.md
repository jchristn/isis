<!-- markdownlint-disable MD033 MD041 -->
<img src="https://raw.githubusercontent.com/jchristn/isis/main/assets/logo.png" width="128" alt="Isis" />

# Isis — Agent Memory Platform

**Version 0.1.0 — ALPHA.**

> ⚠️ **This is alpha software.** APIs, data models, storage layouts, configuration keys, MCP tool names, and dashboard surfaces **will change** — potentially in breaking ways and without migration paths between 0.1.x builds. Pin to an exact image tag (e.g. `v0.1.0`) and read `CHANGELOG.md` before updating.

Isis gives AI models durable, structured, queryable **memory** that survives across sessions, harnesses, and projects. It exposes an **MCP** surface for agents and a **REST** surface (plus a React dashboard) for management.

Memory is organized into **scopes** (a project, a book, or "global"), **categories** (buckets with usage instructions the model reads), and **memories** (atomic notes with tags, links, provenance, and salience). Cross-cutting **policies** (a house writing style, GitHub commit rules) apply everywhere and are surfaced to the agent proactively.

## Images

| Image | Purpose |
|---|---|
| [`jchristn77/isis-server`](https://hub.docker.com/r/jchristn77/isis-server) | REST API (Watson 7.1) + OpenAPI. Listens on `8700`. |
| [`jchristn77/isis-mcp`](https://hub.docker.com/r/jchristn77/isis-mcp) | MCP server (Voltaic 0.6.1), agent-facing tools. Streamable HTTP on `8720`. |
| [`jchristn77/isis-dashboard`](https://hub.docker.com/r/jchristn77/isis-dashboard) | React 19 / Vite 6 management dashboard (nginx). |

All three are published for `linux/amd64` and `linux/arm64`, tagged `v0.1.0` and `latest`. Pin to `v0.1.0`.

The stack also uses [`jchristn77/recalldb-server`](https://hub.docker.com/r/jchristn77/recalldb-server) and [`jchristn77/recalldb-dashboard`](https://hub.docker.com/r/jchristn77/recalldb-dashboard) (`v0.2.1`) for memory content and hybrid search, and a shared [`ankane/pgvector`](https://hub.docker.com/r/ankane/pgvector) Postgres.

## Why

The dominant cost in agentic work is **re-acquiring context** — re-scanning a filesystem, re-reading files, re-learning conventions every session. Isis turns that recurring cost into a one-time write plus cheap recall. Break-even is at roughly the second reuse; after that the savings compound.

## Use cases

- **Code:** remember what lives where, what a function does, and what was already done — so an agent skips the cold re-scan every session.
- **Writing / email / calendar:** Isis is domain-agnostic; the same scope/category/memory model holds notes about a book, an inbox, or a schedule.
- **Cross-cutting guidance:** write a house style, commit rules, or review checklists once as **policies** and recall them across every project.
- **Chat with memory:** ask a scope's memory questions in natural language and get a synthesized answer with citations (RAG over stored memories).

## Backing stores

Isis stores memory through a pluggable `IMemoryStore`, chosen **per scope**:

- **RecallDB** (default) — Postgres-backed; the only provider offering **semantic** and **hybrid** (vector + lexical) search. Isis computes the embedding vector via a configured, health-checked embedding endpoint and passes it to RecallDB (bring-your-own-vector). Embedding dimension is fixed per scope.
- **Verbex** — TF-IDF keyword search, no embeddings.
- **Filesystem** — flat files (single file or a reviewable markdown hierarchy) that travel inside the target repository; keyword/metadata search only.

## Architecture

```
Agent harness ──MCP──▶ nginx ─▶ Isis.McpServer (Voltaic 0.6.1) ─proxy─┐
Operator/UI  ──REST──▶ nginx ─▶ Isis.Server (Watson 7.1) ◀────────────┘
                                     │                 │
                        Isis metadata│                 │memory content + vectors
                          (Postgres  │                 │(RecallDb.Sdk over HTTP)
                           db: isis)  ▼                 ▼
                                ┌───────────┐    ┌──────────────┐
                                │ Postgres  │    │  RecallDB    │
                                │ (shared)  │    │  db: recalldb│
                                └───────────┘    └──────────────┘
   Embedding endpoint ◀─ Isis computes vectors
   Inference endpoint ◀─ Isis summarizes / compacts   (health-checked, dedup by method+URL+auth)
```

- **RecallDB** is the system of record for memory content, embeddings, and retrieval, on a shared Postgres instance.
- **Isis** owns a separate `isis` database on that same Postgres instance for concepts RecallDB has no schema for: category instructions, policies, seed packs, the memory link graph, slugs/titles/summaries, model-endpoint configs, and request history.
- **Isis.McpServer** authenticates the caller and proxies the Isis REST API over loopback.

## Getting started

```bash
git clone https://github.com/jchristn/isis
cd isis/docker
cp .env.example .env        # then edit the secrets
docker compose up -d --build
```

One command brings up Postgres (pgvector), RecallDB + dashboard, the Isis REST/MCP servers and dashboard, two nginx fronts, and the full observability stack (Prometheus, Tempo, Loki, Alloy, Grafana) wired together and healthy. All host-published ports bind `127.0.0.1` (loopback only).

For a seeded demo (a demo tenant, scope, categories, and policies):

```bash
docker compose -f compose.yaml -f factory/compose.factory.yaml up -d --build
```

### Host-published ports (browser-reachable)

| Service | URL | Default credentials |
|---|---|---|
| Isis dashboard | http://localhost:8701 | login `admin@isis.local` / `isisadmin` (tenant `ten_default`) |
| Isis REST (direct) | http://localhost:8700/v1.0/api/health | `x-access-key: isisdefaultkey` + `x-secret-key: isisdefaultsecret` |
| Isis REST (via nginx) | http://localhost:8080 | — |
| Isis MCP (via nginx) | http://localhost:8090/mcp | — |
| Isis MCP (direct) | http://localhost:8720/mcp | — |
| RecallDB console | http://localhost:8601 | `recalldbadmin` |
| Grafana | http://localhost:3000 | `admin` / `admin` |
| Prometheus | http://localhost:9090 | — |
| Tempo API | http://localhost:3200 | — |

> These are **local development defaults**. Change every credential (Grafana password, the seeded admin password, the default credential access/secret keys, RecallDB admin key) before any shared or hosted deployment, and do not expose Prometheus/Tempo/`/metrics` on a public interface.

## Configuration

Isis reads `isis.json` and honors environment overrides. Key variables (see `docker/.env.example`):

| Variable | Meaning |
|---|---|
| `ISIS_AUTH_SEED_ADMIN_EMAIL` / `ISIS_AUTH_SEED_ADMIN_PASSWORD` | Email and password of the seeded bootstrap admin user (defaults `admin@isis.local` / `isisadmin`). Log in for a session token via `POST /v1.0/api/token`. |
| `ISIS_AUTH_DEFAULT_ACCESS_KEY` / `ISIS_AUTH_DEFAULT_SECRET_KEY` | Access key and secret key seeded on the default tenant credential (`x-access-key` + `x-secret-key`; defaults `isisdefaultkey` / `isisdefaultsecret`). |
| `ISIS_DB_TYPE` / `ISIS_DB_SERVER` / `ISIS_DB_DATABASE` / `ISIS_DB_USERNAME` / `ISIS_DB_PASSWORD` | Isis metadata database (Postgresql in Docker). |
| `ISIS_REST_PORT` | REST listener port (default `8700`). |
| `ISIS_MCP_PORT` / `ISIS_MCP_REST_HOSTNAME` / `ISIS_MCP_REST_PORT` | MCP transport port and the REST server it proxies. |
| `RECALLDB_ADMIN_KEY` | RecallDB admin key Isis presents server-side. |
| `GF_SECURITY_ADMIN_PASSWORD` | Grafana admin password. |

## Observability

Watson 7.1 emits the full HTTP surface as metrics and traces; Isis extends it with application meters (memory read/write/search, tokens-served estimate, recall hit-rate, embedding/inference latency, endpoint health gauges). Prometheus scrapes Isis and RecallDB, Tempo ingests traces, Loki aggregates container logs via Alloy, and Grafana is provisioned as code with an **Isis** dashboard folder (starting with an Overview dashboard). Grafana starts only after Prometheus and Tempo are healthy.

## License

MIT — see `LICENSE.md`.
