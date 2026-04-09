---
tracker.kind: file
tracker.project_key: DEMO
tracker.path: tracker/issues
tracker.active_states:
  - Todo
  - In Progress
tracker.terminal_states:
  - Done
  - Closed
  - Cancelled

workspace.root: ./.workspaces
workspace.cleanup_terminal: false

orchestrator.poll_interval_seconds: 60
orchestrator.max_concurrency: 1
orchestrator.max_attempts: 1

# Default worker path stays on built-in dry-run mode until you wire a real local worker.
# To use your real worker, replace agent.command/agent.args with your local CLI while keeping the supported tokens below.
agent.command: dry-run
agent.args:
  - --workspace
  - {workspace}
  - --issue
  - {issue_id}
  - --request
  - {request_path}
agent.max_turns: 1
agent.timeout_ms: 120000

hooks.after_create: dotnet fsi ../../tools/AfterCreate.fsx --workspace . --repo ../..
hooks.before_run: dotnet fsi ../../tools/BeforeRun.fsx --workspace .
hooks.after_run: dotnet fsi ../../tools/AfterRun.fsx --workspace .
hooks.before_remove: dotnet fsi ../../tools/BeforeRemove.fsx --workspace . --repo ../..
hooks.timeout_ms: 60000
---
# Delivery contract

You are operating inside an unattended delivery harness.

## Goal

Turn an eligible issue into either:

1. a review-ready implementation attempt with evidence, or
2. an explicit blocker with evidence and a bounded next step.

## Required behavior

- Work only inside the assigned workspace.
- Read `AGENTS.md` and the referenced docs before making structural changes.
- Reproduce the problem or establish the missing capability first.
- Do not silently widen scope.
- Prefer small, reversible changes.
- Keep architecture and docs aligned with the code.
- Leave evidence for every acceptance item you claim is satisfied.
- Treat one run record as one attempt and one external worker invocation as one turn in this starter.
- `serve` may retry failed active issues up to `orchestrator.max_attempts`; `agent.max_turns > 1` is rejected until a continuation-capable worker runtime exists.
- Do not introduce required `.pi/` settings, pi package configuration, or other non-harness config surfaces for this repository.

## Runtime notes

- The default bootstrap hook provisions a git worktree for a new workspace and leaves reused workspaces unchanged.
- If you enable `workspace.cleanup_terminal`, the default cleanup hook removes git-worktree-backed workspaces with `git worktree remove --force`.
- `poll-once` dispatches up to `orchestrator.max_concurrency` active issues concurrently.
- `serve` reuses the same active-state dispatch rules on repeated ticks and schedules retries with linear backoff equal to `orchestrator.poll_interval_seconds * attempt_number`.
- `status` reads the latest `.harness/runtime/status.json` snapshot, and `serve` appends structured events to `.harness/runtime/host-events.jsonl`.
- `run-issue` and `poll-once` only dispatch issues currently in `tracker.active_states`; file-backed issues with unresolved `depends_on` prerequisites are also rejected for new runs.
- Supported `agent.args` tokens are `{workspace}`, `{issue_id}`, `{issue_title}`, `{request_path}`, and `{project_root}`.
- Prefer whole-value environment-variable expansion such as `$MY_AGENT_COMMAND` for machine-specific command paths in front matter.
- Active runs keep the workflow they were dispatched with; reloads only affect future ticks and future runs.
- If `hooks.after_run` fails, the run is marked failed and the run record captures the hook outcome.

## Tracker adapter notes

- `tracker.kind: file` uses `tracker.path` as a repo-local issue directory, enforces `depends_on` for candidate admission, and can optionally write issue-state transitions when `tracker.write_state_transitions: true` is enabled.
- `tracker.kind: linear` treats `tracker.project_key` as the Linear team key, requires `tracker.api_key` to be an environment-variable reference, and uses `tracker.api_url` only when you need to override the default `https://api.linear.app/graphql` endpoint.
- The current Linear adapter is read-only and supports issue listing, candidate fetch, per-issue refresh, and terminal-issue listing.
- Linear issues populate `issue.id`, `issue.title`, `issue.description`, `issue.state`, and normalized priority/update metadata; `issue.acceptance`, `issue.validation`, and `issue.constraints` remain empty in this pass unless the repo keeps using file-backed issues for that structured metadata.

## Prompt template contract

- If the workflow body contains no double-brace template markers, compatibility mode prepends issue/attempt/turn context and appends the raw workflow body.
- If the workflow body contains template markers, only these variables are supported:
  - `issue.id`
  - `issue.title`
  - `issue.state`
  - `issue.description`
  - `issue.acceptance`
  - `issue.validation`
  - `issue.constraints`
  - `attempt.number`
  - `turn.number`
  - `tracker.kind`
- Unknown variables or malformed double-brace markers fail workflow validation and run start with a named error.

## Host reload contract

- `serve` checks `WORKFLOW.md` for changes on each poll tick.
- Hot-reloaded for future ticks/runs:
  - workflow prompt body
  - `tracker.active_states`
  - `tracker.terminal_states`
  - `tracker.dependency_satisfied_states`
  - `tracker.write_state_transitions`
  - `tracker.claim_state`
  - `tracker.success_state`
  - `tracker.failure_state`
  - `workspace.cleanup_terminal`
  - `orchestrator.poll_interval_seconds`
  - `orchestrator.max_concurrency`
  - `orchestrator.max_attempts`
- Restart-required:
  - `tracker.kind`
  - `tracker.project_key`
  - `tracker.path`
  - `tracker.api_url`
  - `tracker.api_key`
  - `workspace.root`
  - `agent.command`
  - `agent.args`
  - `agent.timeout_ms`
  - `agent.max_turns`
  - `hooks.*`
- Invalid or restart-required reloads keep the last known good workflow active and surface `LastReloadError` through the host status snapshot and structured host log.

## Operator-visible output policy

- Hook and worker output is preserved in workspace-local harness artifacts, with no extra truncation applied by default in this starter.
- Secret-like environment values resolved from whole-token `$VAR` references in workflow front matter are redacted as `[REDACTED]` before operator-visible hook errors, run summaries, and default worker transcripts are written.
- Prompt body and issue text are repo-authored content, not secret-safe channels. Do not place credentials there.

## Canonical issue-state policy

- `Todo` means admitted and runnable when any declared `depends_on` prerequisites are already in `tracker.dependency_satisfied_states`. In the default starter, it is an active state.
- `In Progress` means work has been claimed in the tracker. It is also an active state by default. When `tracker.write_state_transitions` is enabled for `tracker.kind: file`, the starter sets this state automatically on dispatch using `tracker.claim_state`.
- `Blocked` means the issue is not currently runnable because external input, a prerequisite, or a policy decision is missing. It is non-active by default.
- `Human Review` is the review-ready state if a repo chooses to use one. In this starter it is non-active unless the repo explicitly adds it to `tracker.active_states`, and it can be used as `tracker.success_state` when a repo wants review-ready automatic completion without auto-closing to `Done`.
- `Rework` and `Merging` are optional repo-owned coordination states. In this starter they are non-active unless the repo explicitly adds them to `tracker.active_states`.
- `Done`, `Closed`, and `Cancelled` are the default terminal states. They are never dispatched for new work.
- By default, state changes remain human-operated repo/tracker edits. If `tracker.kind: file` and `tracker.write_state_transitions: true` are enabled, the starter writes `tracker.claim_state` on dispatch, `tracker.success_state` after a successful completion, and `tracker.failure_state` after an exhausted failure.
- Retries are host-owned during `serve`: failed active issues are retried with linear backoff until `orchestrator.max_attempts` is reached, dependency-blocked retries wait until their prerequisites satisfy `tracker.dependency_satisfied_states`, and unchanged successful or exhausted active issues are not redispatched again until the tracker issue changes.
- If `workspace.cleanup_terminal` is enabled, terminal workspaces may be removed during cleanup-oriented commands. Non-terminal, non-active states are preserved rather than auto-cleaned.

## Evidence bar

Before handoff, gather:

- relevant build/test output
- file paths changed
- screenshots or recordings for UI changes
- migration proof for schema changes
- benchmark notes when performance is part of acceptance

## Handoff bar

A task is ready for review only when:

- acceptance criteria are addressed,
- required validation commands were run,
- docs were updated if the architecture changed,
- open risks are explicitly listed.

## Scope discipline

Do not fix nearby problems unless:
- they are required to complete the issue, or
- the issue explicitly authorizes the extra work.
