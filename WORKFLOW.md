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
orchestrator.max_attempts: 2

agent.command: dry-run
agent.args:
  - --workspace
  - {workspace}
  - --issue
  - {issue_id}
  - --request
  - {request_path}
agent.max_turns: 4
agent.timeout_ms: 120000

hooks.after_create: dotnet fsi ../../tools/AfterCreate.fsx --workspace . --repo ../..
hooks.before_run: dotnet fsi ../../tools/BeforeRun.fsx --workspace .
hooks.after_run: dotnet fsi ../../tools/AfterRun.fsx --workspace .
hooks.before_remove: dotnet fsi ../../tools/BeforeRemove.fsx --workspace .
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
- Do not introduce required `.pi/` settings, pi package configuration, or other non-harness config surfaces for this repository.

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
