# Project Documentation — Curatarr

This folder is the canonical source of truth for **Curatarr** project state.
Every Curatarr session should start here before making changes.

## Index

| File | Purpose |
|------|---------|
| [`DESIGN.md`](./DESIGN.md) | High-level architecture, component design, decisions & rationale |
| [`TECHNICAL.md`](./TECHNICAL.md) | Implementation details — code layout, endpoints, data model, wiring |
| [`PROGRESS.md`](./PROGRESS.md) | What's done, what's in flight, what's blocked |
| [`PENDING_TESTS.md`](./PENDING_TESTS.md) | Tests needed/skipped/failing + how to run them |
| [`ROADMAP.md`](./ROADMAP.md) | Future goals, deferred ideas, known limitations |
| [`REMAINING-WORK-GROUPS.md`](./REMAINING-WORK-GROUPS.md) | Grouping and sequencing of remaining work |
| [`PLAN-deployment-operations-hardening.md`](./PLAN-deployment-operations-hardening.md) | Approved Group D sequence and implementation plan |
| [`BACKUP-RESTORE.md`](./BACKUP-RESTORE.md) | Operator runbook for SQLite backup, restore, and verification |
| [`TLS-REVERSE-PROXY.md`](./TLS-REVERSE-PROXY.md) | Supported HTTPS options: Tailscale, Caddy, nginx, and Traefik |
| [`CHECKPOINT.md`](./CHECKPOINT.md) | Latest session checkpoint (most recent work + next steps) |
| [`PROMOTION-folder-level-symlinks.md`](./PROMOTION-folder-level-symlinks.md) | ⏸ Pending promotion runbook: Step 0 backup/restore gate, deploy, verify, rollback |

## Template notice

Every project under `/codebase` should maintain a `.project_documents/` folder with
this same set of files (DESIGN, TECHNICAL, PROGRESS, PENDING_TESTS, ROADMAP, and a
CHECKPOINT updated at the end of each session).
