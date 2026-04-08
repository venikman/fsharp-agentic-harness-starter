# F# Agentic Harness Starter

This starter kit separates the **target product** from the **delivery harness**.

- **Target product**: your normal non-agentic software project.
- **Delivery harness**: the repo-owned workflow, tracker intake, per-issue workspaces, validation, evidence, and review loop used by a coding agent.

The starter is intentionally thin:

- the harness code is **F# only**
- the default tracker is **file-backed** so you can prove the loop locally
- the default agent is **dry-run** so you can validate the harness before wiring a real CLI
- `WORKFLOW.md` is the repo-owned contract
- work happens in deterministic per-issue workspaces

## Directory map

```text
.
├── AGENTS.md
├── WORKFLOW.md
├── ISSUE_TEMPLATE.md
├── docs/
│   ├── ARCHITECTURE.md
│   ├── PRODUCT.md
│   ├── QUALITY.md
│   ├── SECURITY.md
│   ├── exec-plans/
│   └── fpf/HARNESS_MODEL.md
├── src/
│   ├── DeliveryHarness.Core/
│   └── DeliveryHarness.Cli/
├── tools/
│   ├── Common.fsx
│   ├── AfterCreate.fsx
│   ├── BeforeRun.fsx
│   ├── AfterRun.fsx
│   └── BeforeRemove.fsx
└── tracker/issues/
```

## What this starter already does

1. Loads a repo-owned `WORKFLOW.md` with front matter and prompt body.
2. Reads issues from `tracker/issues/*.md`.
3. Creates one workspace per issue under `./.workspaces/<issue-id>`.
4. Runs F# hook scripts on workspace lifecycle.
5. Writes an agent request into the workspace.
6. Stores run records under `./.harness/runs/`.

## Quick start

1. Replace placeholders in:
   - `docs/PRODUCT.md`
   - `docs/ARCHITECTURE.md`
   - `docs/QUALITY.md`
   - `WORKFLOW.md`

2. Add real issue files under `tracker/issues/` using `ISSUE_TEMPLATE.md`.

3. Validate the workflow:
   ```bash
   dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- validate-workflow
   ```

4. List issues:
   ```bash
   dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- list-issues
   ```

5. Run a single issue manually:
   ```bash
   dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- run-issue DEMO-0001
   ```

6. Run one polling cycle:
   ```bash
   dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- poll-once
   ```

## First five edits

1. Change `tracker.path`, `workspace.root`, and `agent.command` in `WORKFLOW.md`.
2. Replace the repo copy logic in `tools/AfterCreate.fsx` with your real workspace bootstrap.
3. Replace the validation commands in `docs/QUALITY.md` and in your issue files.
4. Write the actual target-product boundaries in `docs/ARCHITECTURE.md`.
5. Stop using `dry-run` and wire your real coding-agent CLI.

## Suggested rollout

- Wave 0: `DEMO-0002` for a green build baseline and deterministic tests
- Wave 1: `DEMO-0001`, `DEMO-0003`, `DEMO-0004`, `DEMO-0010` for worker/runtime seams and safety
- Wave 2: `DEMO-0005`, `DEMO-0006`, `DEMO-0007`, `DEMO-0008` for long-running orchestration, reconciliation, observability, and reload behavior
- Wave 3: `DEMO-0011`, `DEMO-0012`, `DEMO-0009` for turn semantics, strict prompt templating, and real tracker integration

See `docs/exec-plans/active/DEMO-HARNESS-BACKLOG-ROLLOUT.md` for dependencies and readiness gates.

## Notes

- This starter does **not** try to be a full Symphony clone.
- The workspace bootstrap uses a simple repo copy because it is easy to understand and replace.
- The file tracker is deliberate. It lets you debug your harness before you involve external APIs, credentials, and rate limits.
- Runtime policy stays in harness-owned files. This repo should not depend on checked-in `.pi/` settings for correctness.

## Build target

The starter projects target `net8.0` by default. Retarget if your environment uses a different .NET SDK.
