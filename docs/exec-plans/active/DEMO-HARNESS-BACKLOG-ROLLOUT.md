# Harness backlog rollout plan

## Issue

`DEMO-0001` through `DEMO-0012`

## Goal

Turn the current starter into a long-lived, repo-owned harness product by sequencing the backlog around the same core recommendations reflected in the shared harness-engineering material and the Symphony README/spec: keep policy in-repo, keep the orchestrator separate from the worker runtime, prove the loop locally before expanding trust, and add observability and external integrations only after the local contract is stable.

## Recommendation basis

This rollout plan follows these recommendations from the shared materials:

1. **Repo-owned workflow and docs are the control surface**
   - `WORKFLOW.md`, issue files, and repo docs stay authoritative.
   - Agents should not depend on hidden tribal knowledge or extra config surfaces.

2. **Scheduler/runner concerns stay separate from agent concerns**
   - The harness owns issue intake, claims, workspaces, retries, reconciliation, and run records.
   - The worker runtime only executes one assigned run inside one assigned workspace.

3. **Thin local-first rollout before remote or unattended expansion**
   - Start with file-backed issues, deterministic workspaces, a dry-run path, and manual review.
   - Prove correctness locally before adding real tracker/networked integrations.

4. **Workspace safety and determinism are baseline requirements**
   - Per-issue workspaces are isolated and sanitized.
   - All agent execution must stay inside the configured workspace root.

5. **Evidence and operator visibility matter as much as implementation**
   - Every claimed success needs validations and artifacts.
   - Long-running orchestration needs logs and an operator-visible state surface.

6. **Recovery should work without premature infrastructure**
   - The early harness may rely on in-memory orchestrator state.
   - Restart recovery comes from re-reading the tracker, reconciling, and preserving deterministic workspaces.

7. **No shadow configuration surface**
   - For this repository, checked-in `.pi/` settings and pi package configuration are not part of the runtime contract.
   - If pi is used later, it is only an executable worker invoked by harness-owned config.

## Current baseline

### What already exists

- repo-owned `WORKFLOW.md`
- file-backed issue intake from `tracker/issues/*.md`
- per-issue workspaces under `.workspaces/`
- lifecycle hooks in `tools/*.fsx`
- request-file generation and run-record persistence
- one-shot CLI commands for workflow validation, issue listing, single-issue execution, and one polling cycle

### What is still missing or immature

- green build baseline
- deterministic automated tests
- real external worker integration as the normal path
- git-backed workspace provisioning
- tracker abstraction and non-file tracker support
- long-running orchestration with retries and reconciliation
- structured logs and runtime status
- workflow hot reload
- real attempt/turn semantics
- strict prompt templating

### Current known blocker

`DEMO-0002` must start by restoring a green build baseline. Current observed local failure:
- `dotnet build src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj`
- `src/DeliveryHarness.Core/Agent.fs` reports `FS0072`
- `src/DeliveryHarness.Core/ProcessRunner.fs` produces reserved-identifier warnings for `process`

## Non-negotiable boundaries

- Harness-owned files remain authoritative: `WORKFLOW.md`, `docs/*`, `tracker/issues/*`, and harness-managed environment variables.
- Do not introduce checked-in `.pi/settings.json` or pi package config as required runtime state.
- Keep the file-backed tracker working until a specific issue authorizes broader tracker support.
- Keep the dry-run path usable until the real worker path is proven.
- Do not collapse worker runtime behavior into orchestrator responsibilities.
- Do not make optional observability surfaces required for correctness.

## Phase plan

## Wave 0 — baseline and conformance seed

### Primary issue

- `DEMO-0002` — restore a green build baseline and add deterministic tests

### Why this wave exists

The shared recommendations all assume a harness you can trust to load config, see work, and enforce basic safety rules. Right now the repo has the right shape but not yet the validation spine.

### Detailed outcomes

- current build failure is fixed with the smallest credible change
- a test project exists and runs without network access
- deterministic fixtures cover the starter contract most likely to be broken by future work:
  - workflow loading and defaults
  - issue parsing and ordering
  - workspace sanitization and root containment
  - run-record writing and/or dry-run execution artifacts
- `docs/QUALITY.md` and issue validations explicitly reference the new test path

### Exit gate

Do not start higher-wave issues until:
- `dotnet build` is green
- `dotnet test` is green
- the repo has a stable baseline for future refactors

## Wave 1 — worker/runtime seams and safety

### Issues

- `DEMO-0001` — real external agent CLI support
- `DEMO-0003` — git-based workspace provisioning
- `DEMO-0004` — tracker abstraction
- `DEMO-0010` — stronger workspace/process/secret safety

### Why this wave exists

The recommendations point to a clean separation between orchestration, workspace lifecycle, tracker integration, and worker execution. This wave establishes those seams before the orchestrator becomes long-running.

### Detailed outcomes by issue

#### `DEMO-0001`
- preserve `dry-run` as a proving path
- make external command execution a first-class path
- keep configuration harness-owned
- ensure request-file and workspace information are explicit inputs

#### `DEMO-0003`
- remove repo-copy bootstrap as the default long-term model
- adopt a git-backed bootstrap with documented reuse/reset behavior
- keep workspace prep in hooks rather than pushing repo-specific logic into core modules

#### `DEMO-0004`
- move orchestration away from direct `FileTracker` dependency
- define the minimal tracker port needed for candidate fetch, lookup, refresh, and terminal cleanup
- preserve the markdown tracker as the default proving adapter

#### `DEMO-0010`
- tighten path, process, and secret-handling rules
- make hook/process failure behavior operator-visible and testable
- revalidate workspace-root containment before execution, not just during creation

### Safe parallelism inside Wave 1

Recommended pairs:
- `DEMO-0001` with `DEMO-0003`
- `DEMO-0004` with `DEMO-0010`

Avoid concurrent work if both changes touch the same seam at once, especially:
- hook execution behavior
- workspace lifecycle internals
- tracker-facing orchestration calls

### Exit gate

Do not start Wave 2 until:
- a real worker command path exists
- workspace provisioning is no longer repo-copy dependent
- tracker orchestration depends on an abstraction seam
- safety rules are explicit and tested

## Wave 2 — long-running orchestration lifecycle

### Issues

- `DEMO-0005` — long-running orchestrator mode and retry backoff
- `DEMO-0006` — active reconciliation and terminal cleanup
- `DEMO-0007` — structured logs and status surface
- `DEMO-0008` — workflow hot reload with last-known-good behavior

### Why this wave exists

The shared recommendations and Symphony spec both center the orchestrator as a long-running service that owns claims, retries, reconciliation, and visibility. That should only happen after Wave 1 seams are in place.

### Detailed outcomes by issue

#### `DEMO-0005`
- add a durable host/daemon mode beyond `poll-once`
- make `orchestrator.max_attempts` and retry timing real runtime behavior
- preserve `poll-once` for debugging and smoke validation

#### `DEMO-0006`
- refresh active work against tracker state on each tick
- stop and clean terminal work safely
- stop non-active work without destructive cleanup

#### `DEMO-0007`
- add structured logs with issue/run context
- expose enough runtime state for an operator to debug the harness without attaching a debugger
- keep dashboards or status UIs optional

#### `DEMO-0008`
- watch and reload `WORKFLOW.md`
- keep the last known good config active when a reload fails
- document what is hot-reloaded versus restart-required

### Parallelism guidance inside Wave 2

- `DEMO-0007` may progress after the basic runtime state model in `DEMO-0005` stabilizes.
- `DEMO-0008` should not land on top of rapidly changing orchestration state without coordination.
- `DEMO-0006` should be treated as coupled to `DEMO-0005` and `DEMO-0004`.

### Exit gate

Do not start Wave 3 until:
- the harness has a credible long-running mode
- retries and reconciliation are not just documentation
- operator-visible status exists
- reload behavior no longer requires service restart for routine workflow edits

## Wave 3 — contract refinement and external tracker integration

### Issues

- `DEMO-0011` — explicit turn semantics and meaningful `agent.max_turns`
- `DEMO-0012` — strict prompt templating with issue/attempt variables
- `DEMO-0009` — Linear-compatible tracker adapter

### Why this wave exists

Only after worker execution and long-running orchestration are real should the harness tighten its prompt contract and connect to a real external tracker.

### Detailed outcomes by issue

#### `DEMO-0011`
- define the difference between an attempt and a turn
- make `agent.max_turns` either real or explicitly unsupported
- make run/session metadata sufficient for operators and future prompts

#### `DEMO-0012`
- replace loose prompt concatenation with a small, strict template surface
- ensure prompt variables reflect actual runtime state rather than imagined state
- fail fast on unknown variables

#### `DEMO-0009`
- add a real tracker read path while keeping file-backed proving intact
- keep credentials in environment-backed configuration
- avoid baking tracker writes into the orchestrator core

### Exit gate

Wave 3 is complete when:
- the runtime contract for attempts, turns, and prompts matches actual behavior
- a real tracker can feed the harness without replacing the proving path
- external integration does not weaken the documented trust boundary

## Dependency map

| Issue | Depends on | Why |
|---|---|---|
| `DEMO-0002` | none | establishes a trustworthy baseline |
| `DEMO-0001` | `DEMO-0002` | real worker integration should start from a green build |
| `DEMO-0003` | `DEMO-0002` | workspace changes need baseline confidence |
| `DEMO-0004` | `DEMO-0002` | abstraction refactors need baseline confidence |
| `DEMO-0010` | `DEMO-0002` | safety hardening should land on a stable baseline |
| `DEMO-0005` | `DEMO-0002`, `DEMO-0004` | long-running orchestration should target the tracker seam |
| `DEMO-0006` | `DEMO-0005`, `DEMO-0004` | reconciliation depends on orchestrator state plus tracker refresh |
| `DEMO-0007` | `DEMO-0005` | useful status/logging depends on long-running state |
| `DEMO-0008` | `DEMO-0005` | reload behavior matters once the orchestrator stays alive |
| `DEMO-0011` | `DEMO-0001`, `DEMO-0005` | turn semantics depend on a real worker and real orchestration |
| `DEMO-0012` | `DEMO-0011` | strict prompt variables should reflect the actual attempt/turn model |
| `DEMO-0009` | `DEMO-0004`, `DEMO-0010` | external tracker integration should use the abstraction seam and hardened secret handling |

## Evidence expectations by wave

### Wave 0
- green `dotnet build`
- green `dotnet test`
- fixture/test paths listed in handoff

### Wave 1
- worker-run evidence paths under workspace `.harness/`
- workspace bootstrap evidence
- tracker abstraction tests or smoke checks
- safety failure-path evidence

### Wave 2
- long-running mode validation output
- retry/reconciliation evidence
- operator-visible logs or snapshots
- workflow reload proof including invalid-reload behavior

### Wave 3
- prompt rendering tests/evidence
- run/session metadata showing attempts/turns
- tracker smoke or integration evidence with secrets redacted

## Immediate next action

Start with `DEMO-0002` using a bounded issue-level execution plan. The goal is not to add more capability yet; it is to make future capability work safe and reviewable.

## Risks

- risk: higher-wave issues start before the baseline is trustworthy
  - mitigation: treat `DEMO-0002` as a non-optional gate
- risk: worker-runtime logic and orchestrator logic drift together
  - mitigation: preserve tracker, worker, and workspace seams in docs and code
- risk: convenience config outside the harness becomes required runtime state
  - mitigation: keep all runtime policy in harness-owned files only
- risk: external integration arrives before observability and safety are mature
  - mitigation: keep real tracker work in Wave 3 after logging, retries, and reload behavior exist
