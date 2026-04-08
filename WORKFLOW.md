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
- Treat one harness run as one attempt and one external worker invocation in this starter; `orchestrator.max_attempts` and `agent.max_turns` above `1` are not yet supported.
- Do not introduce required `.pi/` settings, pi package configuration, or other non-harness config surfaces for this repository.

## Runtime notes

- The default bootstrap hook provisions a git worktree for a new workspace and leaves reused workspaces unchanged.
- If you enable `workspace.cleanup_terminal`, the default cleanup hook removes git-worktree-backed workspaces with `git worktree remove --force`.
- Supported `agent.args` tokens are `{workspace}`, `{issue_id}`, `{issue_title}`, `{request_path}`, and `{project_root}`.
- Prefer whole-value environment-variable expansion such as `$MY_AGENT_COMMAND` for machine-specific command paths in front matter.
- `orchestrator.poll_interval_seconds` is reserved for future loop-mode work and is not used by the current one-shot commands.

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
