# Issue backlog guide

This directory is the harness-owned backlog for the current starter.

## Rules

- Issue files are the source of truth for work intake.
- `README.md` in this directory is documentation only and is ignored by the file tracker.
- Runtime policy belongs in harness-owned files such as `WORKFLOW.md`, `docs/*`, and harness-managed environment variables.
- Do not add checked-in `.pi/` settings or pi package configuration as required runtime state for this repository.
- Today the starter runtime reads `id`, `title`, `state`, `priority`, `depends_on`, `acceptance`, `validation`, `constraints`, and the body text.
- For `tracker.kind: file`, `depends_on` is enforced for candidate admission and manual `run-issue` dispatch. `fpf.*` fields remain repo-delivery metadata only.

## Current rollout order

1. `DEMO-0002` — completed Wave 0 baseline reference for the green build and deterministic validation bundle
2. `DEMO-0001`, `DEMO-0003`, `DEMO-0004`, `DEMO-0010` — completed worker, workspace, tracker, and safety baseline references
3. `DEMO-0005`, `DEMO-0006`, `DEMO-0007`, `DEMO-0008`, `DEMO-0011`, `DEMO-0012` — completed host/runtime and workflow-contract baseline references
4. `DEMO-0009` — completed optional external-tracker baseline reference if the repo needs more than the file-backed proving path
5. `DEMO-9999` — completed trusted-local release-proof reference for the final one-start verification pass
6. `DEMO-0013` — completed follow-up reference for file-backed dependency admission and optional tracker state transitions

For the fuller dependency map, read:
- `docs/exec-plans/completed/DEMO-HARNESS-BACKLOG-ROLLOUT.md`

## Issue authoring reminders

- Keep acceptance measurable.
- Keep validation commands exact.
- Record scope constraints explicitly.
- Use `depends_on` for real prerequisite gating in file-backed mode and mention any nuance in the body when a task is not safe to run early.
- Keep file-backed tracker support working until a specific issue authorizes replacing it.
