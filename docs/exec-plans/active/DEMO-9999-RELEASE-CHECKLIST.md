# DEMO-9999 release checklist

This checklist is the review artifact for the claim:

> Start the harness once in trusted-local mode and get the expected project-level outcome or an explicit bounded blocker report with evidence.

Do not mark `DEMO-9999` complete until every required item below is checked and linked to concrete evidence.

## A. Prerequisite issue closure

Required issues must be in a terminal state or explicitly closed with a documented blocker and bounded next step:

- [ ] `DEMO-0002` — baseline build/test trust exists
- [ ] `DEMO-0001` — real worker command path exists
- [ ] `DEMO-0003` — real workspace bootstrap exists
- [ ] `DEMO-0004` — tracker seam exists
- [ ] `DEMO-0010` — safety baseline exists
- [ ] `DEMO-0005` — long-running host mode exists
- [ ] `DEMO-0006` — reconciliation exists
- [ ] `DEMO-0007` — observability/status exists
- [ ] `DEMO-0008` — workflow reload behavior exists
- [ ] `DEMO-0011` — attempt/turn semantics are honest
- [ ] `DEMO-0012` — prompt contract is strict

Optional, only if the target outcome requires a real external tracker:

- [ ] `DEMO-0009` — Linear-compatible tracker adapter exists

## B. Exact startup contract

Record the exact one-start command here once `DEMO-0005` lands:

```bash
# Replace with the final command
# dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- <host-command>
```

Checklist:
- [ ] one exact startup command is documented
- [ ] command uses harness-owned configuration only
- [ ] no checked-in `.pi/` settings or pi package configuration are required

## C. Issue-state transition policy

The repository must publish one explicit issue-state policy covering at least:

- [ ] when an issue becomes `In Progress`
- [ ] what “review-ready” means, if used
- [ ] what counts as `Blocked`
- [ ] what terminal states are used in this repo
- [ ] whether state changes are runtime-automated, human-operated, or hybrid
- [ ] how retries relate to issue state

Record the canonical policy location(s):
- [ ] `WORKFLOW.md`
- [ ] repo docs (list exact path in handoff)

## D. Required validation bundle

These checks must succeed unless explicitly blocked with evidence:

- [ ] `dotnet restore src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj`
- [ ] `dotnet build src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj --no-restore`
- [ ] `dotnet test`
- [ ] `dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- validate-workflow`
- [ ] `dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- list-issues`
- [ ] final host-mode startup command

## E. End-to-end smoke run evidence

Run the harness once against a controlled eligible issue set and capture:

- [ ] startup command output
- [ ] host-mode logs/status output
- [ ] workspace `.harness/*` artifact paths for exercised issues
- [ ] `.harness/runs/*.json` run-record paths
- [ ] final issue-state summary
- [ ] statement of outcome:
  - [ ] expected project-level result achieved
  - [ ] or bounded blocker report produced

## F. Final review questions

Answer all before closure:

- [ ] Can a reviewer reproduce the startup path from repo-owned docs alone?
- [ ] Does the final claim rely only on harness-owned configuration?
- [ ] Are remaining risks called out for broader unattended or remote use?
- [ ] Is rollback/recovery guidance present for tracker, workflow, workspace, and worker changes where relevant?

## Handoff note template

Use this in the final review handoff:

- startup command:
- issue-state policy location:
- validations run:
- exercised issue set:
- evidence paths:
- final outcome:
- remaining risks:
