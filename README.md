# F# Agentic Harness Starter

This starter kit separates the **target product** from the **delivery harness**.

- **Target product**: your normal non-agentic software project.
- **Delivery harness**: the repo-owned workflow, tracker intake, per-issue workspaces, validation, evidence, and review loop used by a coding agent.

The starter is intentionally thin:

- the harness code is **F# only**
- the default tracker is **file-backed** so you can prove the loop locally
- the default agent is **dry-run** so you can validate the harness before wiring a real CLI
- the baseline starter is **single-attempt / single-turn**; `orchestrator.max_attempts` and `agent.max_turns` must stay `1` until richer orchestration lands
- `WORKFLOW.md` is the repo-owned contract
- work happens in deterministic per-issue workspaces

## Directory map

```text
.
├── AGENTS.md
├── WORKFLOW.md
├── ISSUE_TEMPLATE.md
├── DeliveryHarness.sln
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
├── tests/
│   └── DeliveryHarness.Tests/
├── tools/
│   ├── Common.fsx
│   ├── AfterCreate.fsx
│   ├── BeforeRun.fsx
│   ├── AfterRun.fsx
│   ├── BeforeRemove.fsx
│   └── StubWorker.fsx
└── tracker/issues/
```

## What this starter already does

1. Loads a repo-owned `WORKFLOW.md` with front matter and prompt body.
2. Reads issues from `tracker/issues/*.md`.
3. Provisions one workspace per issue under `./.workspaces/<issue-id>` using the default git-worktree bootstrap hook.
4. Leaves reused workspaces unchanged unless you remove them explicitly.
5. Runs trusted F# hook scripts on workspace lifecycle.
6. Writes an agent request into the workspace.
7. Supports both `dry-run` and generic external worker commands.
8. Stores run records under `./.harness/runs/` for both success and failure paths once a run starts.

## Quick start

1. Replace placeholders in:
   - `docs/PRODUCT.md`
   - `docs/ARCHITECTURE.md`
   - `docs/QUALITY.md`
   - `WORKFLOW.md`

2. Add real issue files under `tracker/issues/` using `ISSUE_TEMPLATE.md`.

3. Build the starter:
   ```bash
   dotnet build src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj
   ```

4. Run the deterministic harness suite:
   ```bash
   dotnet test
   ```

5. Validate the workflow:
   ```bash
   dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- validate-workflow
   ```

6. List issues:
   ```bash
   dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- list-issues
   ```

7. Run a single issue manually:
   ```bash
   dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- run-issue DEMO-0001
   ```

8. Run one polling cycle:
   ```bash
   dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- poll-once
   ```

9. To smoke the real worker path without external dependencies, temporarily point `agent.command` to `dotnet` and `agent.args` to `tools/StubWorker.fsx`, or run the same configuration through a temporary workflow override.

## Generic worker contract

The starter keeps the worker protocol intentionally small:

- the harness writes `<workspace>/.harness/agent-request.md`
- the worker runs with `cwd = workspace`
- `agent.command` and `agent.args` stay repo-owned in `WORKFLOW.md`
- supported `agent.args` tokens are:
  - `{workspace}`
  - `{issue_id}`
  - `{issue_title}`
  - `{request_path}`
  - `{project_root}`
- stdout/stderr are captured into `<workspace>/.harness/agent-output.txt`
- `dry-run` stays as the proving/smoke mode
- `tools/StubWorker.fsx` is a local fake worker for deterministic validation of the non-dry-run path

## Current starter limits

- `orchestrator.max_attempts` must stay `1`; values above `1` are rejected by workflow validation and by run start.
- `agent.max_turns` must stay `1`; values above `1` are rejected by workflow validation and by run start.
- `orchestrator.poll_interval_seconds` is reserved for future long-running loop mode and is not used by the current one-shot commands.
- issue `depends_on` and `fpf.*` metadata are repo-delivery metadata today; the local starter runtime does not enforce them.

## First five edits

1. Change `tracker.path`, `workspace.root`, and `agent.command` in `WORKFLOW.md`.
2. Decide whether to keep the default git-worktree bootstrap in `tools/AfterCreate.fsx` or replace it with your repo-specific local bootstrap.
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
- The default workspace bootstrap uses `git worktree` because it is deterministic, local-first, and easier to review than repo-copy provisioning.
- Reused workspaces are left unchanged by default; the starter does not silently reset them.
- If you enable `workspace.cleanup_terminal`, the default cleanup hook removes git-worktree-backed workspaces with `git worktree remove --force`.
- The file tracker is deliberate. It lets you debug your harness before you involve external APIs, credentials, and rate limits.
- Runtime policy stays in harness-owned files. This repo should not depend on checked-in `.pi/` settings for correctness.
- Prefer whole-value environment-variable expansion in `WORKFLOW.md` for machine-specific command paths.
- Run records under `.harness/runs/` are written for both success and failure paths when a run attempt starts.

## Build target

The starter projects target `net8.0` by default. Retarget if your environment uses a different .NET SDK.
