# Architecture

## Named contexts

- `Ctx.TargetProduct.DeliveryHarness`
  - the shipped harness product
  - owns workflow loading, issue intake, workspace management, agent execution, orchestration, and observability

- `Ctx.ProjectDelivery.DeliveryHarnessRepo`
  - the delivery system used to evolve this repository
  - owns issue files, execution plans, validation evidence, and review handoff for changes to the harness itself

Do not collapse these contexts. Product code stays generic; repo-delivery files describe and constrain how this repo is changed.

## Harness modules

- `DeliveryHarness.Core`
  - domain types and invariants
  - front matter parsing and workflow loading
  - tracker intake and normalization
  - workspace management and safety checks
  - agent request construction and process execution
  - orchestration and run-record persistence

- `DeliveryHarness.Cli`
  - operator entry points
  - workflow validation
  - manual issue execution
  - one-shot polling plus long-running host/status commands

- `tools/*.fsx`
  - replaceable workspace lifecycle hooks
  - bootstrap, pre-run, post-run, and cleanup behavior

## Runtime flow

```text
issue file or tracker item
  -> workflow/config load
  -> workspace create or reuse
  -> workspace hooks
  -> prompt/request assembly
  -> agent process execution
  -> evidence and run record capture
  -> human review or next workflow state
```

## Current baseline

- tracker seam: `DeliveryHarness.Core/Tracker.fs` defines the harness-owned port; the default adapter remains file-backed markdown issues and a read-only Linear adapter is now available for external intake
- workspace bootstrap: git-worktree provisioning in `tools/AfterCreate.fsx`, with reused workspaces left unchanged
- agent runner: generic external process with `dry-run` default until a repo wires its real local worker command
- orchestration mode: `run-issue`, `poll-once`, and `serve`; host mode keeps in-memory running/retrying/retired state, enforces bounded concurrency, performs linear retry backoff, and reconciles tracker state on each tick
- observability: structured host events plus a JSON status snapshot live under `.harness/runtime/`
- workflow contract: prompt templates support strict `{{ ... }}` variables with compatibility mode, and host mode hot-reloads future ticks/runs while preserving a last-known-good config on invalid reloads
- safety/output policy: hooks and worker launches revalidate workspace containment, and operator-visible output is deterministically redacted for workflow-configured secret-like env values
- run records: structured evidence including real attempt numbers, explicit turn numbers, performer/context metadata, and hook outcomes
- review/merge: manual

## Planned evolution

1. keep repo docs concrete enough for unattended agents
2. extend deterministic validation and smoke sample inputs as the harness contract evolves
3. keep git-backed workspace provisioning and cleanup policy explicit as real repos adopt the starter
4. preserve the file-backed proving path as the default local contract even when real tracker adapters such as the current read-only Linear path are enabled
5. prove the final trusted-local `DEMO-9999` startup/review bundle against the now-landed host/runtime contract
6. add richer continuation-capable worker semantics only if the repo truly needs multi-turn orchestration

## Pi integration boundary

Pi is an optional worker runtime, not the orchestrator.

- The harness owns all durable configuration: workflow settings, tracker selection, workspace rules, retries, concurrency, validation requirements, and safety policy.
- If `agent.command` targets `pi`, the harness passes context in through command arguments, request files, and harness-controlled environment variables.
- Checked-in `.pi/settings.json`, pi packages, or extension-local config must not be required for correctness.
- Pi-side customizations, if used, must stay behavior-only helpers scoped to one assigned workspace and must not poll trackers, claim work, schedule retries, or own global run state.

## Dependency rules

- `DeliveryHarness.Cli` may depend on `DeliveryHarness.Core`.
- `DeliveryHarness.Core` must not depend on project-specific product code outside the harness.
- Repo docs define policy and scope. Product code implements mechanics.
- Workflow hooks are trusted configuration, not compile-time product dependencies.
- Workspaces remain disposable and must stay inside the configured workspace root.

## Integration seams to preserve

- tracker adapter boundary between orchestration and issue-source details
- agent runner boundary between harness orchestration and a specific coding-agent protocol
- workspace bootstrap boundary between core safety rules and repo-specific setup logic
- observability boundary between orchestrator state and optional dashboards or APIs
