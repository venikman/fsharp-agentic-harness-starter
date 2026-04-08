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
  - polling and future service-host commands

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

- tracker adapter: file-backed markdown issues
- workspace bootstrap: repo copy in `tools/AfterCreate.fsx`
- agent runner: generic external process with `dry-run` default
- orchestration mode: one-shot commands, sequential by default
- review/merge: manual

## Planned evolution

1. keep repo docs concrete enough for unattended agents
2. add deterministic tests and smoke fixtures for the harness itself
3. replace repo-copy bootstrap with git-backed workspace provisioning
4. introduce a tracker abstraction while preserving the file adapter
5. evolve from one-shot commands to long-running orchestration with retries and reconciliation
6. add structured logs and optional status surfaces without making them required for correctness

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
