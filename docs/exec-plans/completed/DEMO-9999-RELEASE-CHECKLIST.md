# DEMO-9999 release checklist

This checklist is the review artifact for the claim:

> Start the harness once in trusted-local mode and get the expected project-level outcome or an explicit bounded blocker report with evidence.

Do not mark `DEMO-9999` complete until every required item below is checked and linked to concrete evidence.

## A. Prerequisite issue closure

Required issues must be in a terminal state or explicitly closed with a documented blocker and bounded next step:

- [x] `DEMO-0002` — baseline build/test trust exists
- [x] `DEMO-0001` — real worker command path exists
- [x] `DEMO-0003` — real workspace bootstrap exists
- [x] `DEMO-0004` — tracker seam exists
- [x] `DEMO-0010` — safety baseline exists
- [x] `DEMO-0005` — long-running host mode exists
- [x] `DEMO-0006` — reconciliation exists
- [x] `DEMO-0007` — observability/status exists
- [x] `DEMO-0008` — workflow reload behavior exists
- [x] `DEMO-0011` — attempt/turn semantics are honest
- [x] `DEMO-0012` — prompt contract is strict

Optional, only if the target outcome requires a real external tracker:

- [x] `DEMO-0009` — Linear-compatible tracker adapter exists

## B. Exact startup contract

Record the exact one-start command here:

```bash
dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- serve
```

Checklist:
- [x] one exact startup command is documented
- [x] command uses harness-owned configuration only
- [x] no checked-in `.pi/` settings or pi package configuration are required

## C. Issue-state transition policy

The repository must publish one explicit issue-state policy covering at least:

- [x] when an issue becomes `In Progress`
- [x] what “review-ready” means, if used
- [x] what counts as `Blocked`
- [x] what terminal states are used in this repo
- [x] whether state changes are runtime-automated, human-operated, or hybrid
- [x] how retries relate to issue state

Record the canonical policy location(s):
- [x] `WORKFLOW.md`
- [x] repo docs (`README.md`)

## D. Required validation bundle

These checks must succeed unless explicitly blocked with evidence:

- [x] `dotnet restore src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj`
- [x] `dotnet build src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj --no-restore`
- [x] `dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- validate-workflow`
- [x] `dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- list-issues`
- [x] final host-mode startup command

## E. End-to-end smoke run evidence

Run the harness once against a controlled eligible issue set and capture:

- [x] startup command output
- [x] host-mode logs/status output
- [x] workspace `.harness/*` artifact paths for exercised issues
- [x] `.harness/runs/*.json` run-record paths
- [x] final issue-state summary
- [x] statement of outcome:
  - [x] expected project-level result achieved
  - [ ] or bounded blocker report produced

## F. Final review questions

Answer all before closure:

- [x] Can a reviewer reproduce the startup path from repo-owned docs alone?
- [x] Does the final claim rely only on harness-owned configuration?
- [x] Are remaining risks called out for broader unattended or remote use?
- [x] Is rollback/recovery guidance present for tracker, workflow, workspace, and worker changes where relevant?

## Handoff note

- startup command: `dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- serve`
- issue-state policy location: `WORKFLOW.md` (canonical), `README.md` (pointer + recovery notes)
- validations run:
  - `dotnet restore src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj`
  - `dotnet build src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj --no-restore`
  - `dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- validate-workflow`
  - `dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- list-issues`
  - `dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- run-issue DEMO-0009`
  - bounded `dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- serve` smoke
  - `dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- status`
- exercised issue set: `DEMO-0009`, `DEMO-9999`
- evidence paths:
  - `.harness/runs/20260408T120241418Z-DEMO-0009.json`
  - `.harness/runs/20260408T122836999Z-DEMO-9999.json`
  - `.harness/runtime/status.json`
  - `.harness/runtime/host-events.jsonl`
  - `.workspaces/DEMO-9999/.harness/agent-request.md`
  - `.workspaces/DEMO-9999/.harness/dry-run.txt`
  - `.workspaces/DEMO-9999/.harness/before-run.txt`
  - `.workspaces/DEMO-9999/.harness/after-run.txt`
  - `.workspaces/DEMO-9999/.harness/after-create.txt`
- final outcome: expected trusted-local harness result achieved for the file-backed and read-only Linear-backed paths; the host starts, dispatches active work, writes run records plus runtime status/log artifacts, and the repo-owned contract matches actual behavior.
- remaining risks:
  - trusted-local only; remote or unattended production use still needs stronger isolation and operating controls
  - worker turns remain single-invocation only; `agent.max_turns > 1` still requires a continuation-capable runtime
  - the Linear adapter is read-only in this pass and does not write tracker comments or state transitions
