# F# Agentic Harness Starter

This starter kit separates the **target product** from the **delivery harness**.

- **Target product**: your normal non-agentic software project.
- **Delivery harness**: the repo-owned workflow, tracker intake, per-issue workspaces, validation, evidence, and review loop used by a coding agent.

The starter is intentionally thin:

- the harness code is **F# only**
- the default tracker is **file-backed** so you can prove the loop locally, and a read-only Linear adapter is available when you need a real external tracker
- the default agent is **dry-run** so you can validate the harness before wiring a real CLI
- the generic worker contract stays **single-turn per external worker invocation**; host-mode retries make `orchestrator.max_attempts` real, while `agent.max_turns > 1` is still rejected until a continuation-capable runtime exists
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
2. Reads issues through a harness-owned tracker seam from either `tracker/issues/*.md` or a read-only Linear-compatible GraphQL source.
3. Provisions one workspace per issue under `./.workspaces/<issue-id>` using the default git-worktree bootstrap hook.
4. Leaves reused workspaces unchanged unless you remove them explicitly.
5. Runs trusted F# hook scripts on workspace lifecycle.
6. Writes an agent request into the workspace.
7. Supports both `dry-run` and generic external worker commands.
8. Stores structured run records under `./.harness/runs/` for both success and failure paths once a run starts.
9. Dispatches up to `orchestrator.max_concurrency` active issues concurrently during `poll-once` and `serve`.
10. Exposes `serve` for long-running host mode and `status` for reading the latest runtime snapshot.
11. Retries failed host-mode attempts with linear backoff while respecting `orchestrator.max_attempts`.
12. Reconciles running work against tracker state and applies terminal-workspace cleanup according to policy.
13. Writes structured host logs and status snapshots under `./.harness/runtime/`.
14. Hot-reloads `WORKFLOW.md` for future ticks/runs while preserving the last known good config on invalid reloads.
15. Supports strict `{{ ... }}` prompt templates with a documented compatibility path for legacy workflow bodies.
16. Redacts workflow-configured secret-like env values from operator-visible summaries and default transcripts without truncating output by default.

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
   dotnet run --project tests/DeliveryHarness.Tests/DeliveryHarness.Tests.fsproj
   ```

5. Validate the workflow:
   ```bash
   dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- validate-workflow
   ```

6. List issues:
   ```bash
   dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- list-issues
   ```

7. Run a single active issue manually:
   ```bash
   dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- run-issue ACTIVE_ISSUE_ID
   ```

8. Run one polling cycle:
   ```bash
   dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- poll-once
   ```

9. Start the long-running host:
   ```bash
   dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- serve
   ```

10. Inspect the latest host snapshot:
   ```bash
   dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- status
   ```

11. To smoke the real worker path without external dependencies, temporarily point `agent.command` to `dotnet` and `agent.args` to `tools/StubWorker.fsx`, or run the same configuration through a temporary workflow override.

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

## Optional Linear tracker

If you need a real external tracker without giving up the file-backed proving path, switch the workflow front matter to:

```yaml
tracker.kind: linear
tracker.project_key: ENG
tracker.api_key: $LINEAR_API_KEY
tracker.api_url: https://api.linear.app/graphql
```

Notes:

- `tracker.project_key` maps to the Linear team key for this adapter.
- `tracker.api_key` must be an environment-variable reference; do not inline the token in repo files.
- The adapter is read-only in this pass: it supports issue listing, candidate fetch, per-issue refresh, and terminal-issue listing.
- Linear issues currently normalize identifier, title, description, state, priority, updated time, and source URL; `acceptance`, `validation`, and `constraints` remain empty unless you keep using file-backed issues.
- The checked-in repo workflow stays file-backed by default so local tests and smoke runs do not require network access or credentials.

## Current starter limits

- one external worker invocation still equals one turn, so `agent.max_turns` must stay `1` until a continuation-capable worker runtime exists
- retries are host-mode behavior; `run-issue` and `poll-once` remain one-shot commands
- host scheduling state is in-memory only; restart recovery comes from re-reading tracker state and reusing deterministic workspaces
- issue `depends_on` and `fpf.*` metadata are repo-delivery metadata today; the local starter runtime does not enforce them
- the Linear adapter is intentionally read-only in this pass; tracker comments, state writes, and PR-link automation remain out of scope

## First five edits

1. Change `tracker.path`, `workspace.root`, and `agent.command` in `WORKFLOW.md`.
2. Decide whether to keep the default git-worktree bootstrap in `tools/AfterCreate.fsx` or replace it with your repo-specific local bootstrap.
3. Replace the validation commands in `docs/QUALITY.md` and in your issue files.
4. Write the actual target-product boundaries in `docs/ARCHITECTURE.md`.
5. Stop using `dry-run` and wire your real coding-agent CLI.

## Suggested rollout

- Wave 0 is already landed: `DEMO-0002` established the green-build and deterministic-test baseline.
- Wave 1 is already landed: `DEMO-0001`, `DEMO-0003`, `DEMO-0004`, and `DEMO-0010` define the current worker, workspace, tracker, and safety seams.
- Wave 2 and the local contract-refinement work are now landed: `DEMO-0005`, `DEMO-0006`, `DEMO-0007`, `DEMO-0008`, `DEMO-0011`, and `DEMO-0012` provide host mode, reconciliation, observability, reload behavior, honest attempt/turn semantics, and strict prompt templating.
- The harness backlog in this starter repo is now closed for the file-backed default path and the optional read-only Linear intake path. `DEMO-9999` remains as the historical release-proof reference rather than an open substrate issue.

See `docs/exec-plans/completed/DEMO-HARNESS-BACKLOG-ROLLOUT.md` for dependencies and readiness gates.

## Notes

- This starter does **not** try to be a full Symphony clone.
- The default workspace bootstrap uses `git worktree` because it is deterministic, local-first, and easier to review than repo-copy provisioning.
- Reused workspaces are left unchanged by default; the starter does not silently reset them.
- If you enable `workspace.cleanup_terminal`, the default cleanup hook removes git-worktree-backed workspaces with `git worktree remove --force`.
- The file tracker remains deliberate as the default path. It lets you debug your harness before you involve external APIs, credentials, and rate limits, even though the read-only Linear adapter now exists.
- Runtime policy stays in harness-owned files. This repo should not depend on checked-in `.pi/` settings for correctness.
- Prefer whole-value environment-variable expansion in `WORKFLOW.md` for machine-specific command paths.
- Use `list-issues` to choose an id from `tracker.active_states`; `run-issue` rejects non-active issues.
- Run records under `.harness/runs/` are written for both success and failure paths when a run attempt starts.
- `serve` writes `.harness/runtime/host-events.jsonl` and `.harness/runtime/status.json`; `status` reads the latest snapshot.
- `WORKFLOW.md` is the canonical location for the issue-state policy, host reload contract, and prompt-template contract. This README is only a pointer.
- Workflow prompt bodies without `{{ ... }}` markers keep the legacy wrapped issue-context format; strict templates only support the documented variable set.
- If `hooks.after_run` fails, the run is marked failed and the hook outcome is recorded in the run record.
- Secret-like env values referenced from workflow front matter are redacted from operator-visible hook/worker summaries and default transcripts, but prompt-body content is not scrubbed for you.
- Recovery notes:
  - tracker: if an external tracker configuration fails, switch `tracker.kind` back to `file`, restore env-backed secrets, and rerun `validate-workflow`.
  - workflow: invalid hot reloads keep the last known good workflow active; restart `serve` after any restart-required workflow change.
  - workspace: reused workspaces are left unchanged; remove a specific workspace under `.workspaces/` or enable terminal cleanup if you need a fresh bootstrap.
  - worker: switch `agent.command` back to `dry-run` to recover from a broken worker configuration while keeping the harness loop provable.

## Build target

The starter projects target `net8.0` by default. Retarget if your environment uses a different .NET SDK.
