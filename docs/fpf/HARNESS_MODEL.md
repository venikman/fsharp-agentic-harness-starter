# FPF harness model

## Named contexts

- `Ctx.TargetProduct.<name>`
  - `U.System`: the shipped application, API, library, or CLI

- `Ctx.ProjectDelivery.<name>`
  - `U.System`: tracker + orchestrator + workspace manager + repo-owned workflow contract + evidence/handoff loop

## FPF mapping

| FPF type | Starter harness interpretation |
|---|---|
| `U.RoleAssignment` | human steward, orchestrator, coding agent, reviewer |
| `U.Capability` | turn an eligible issue into a validated implementation attempt |
| `U.PromiseContent` | produce review-ready change + evidence, or blocker + evidence |
| `U.Method` | claim -> bootstrap -> reproduce -> plan -> implement -> validate -> handoff |
| `U.MethodDescription` | `WORKFLOW.md`, `AGENTS.md`, `docs/`, exec plans |
| `U.WorkPlan` | issue file + acceptance + validation + constraints |
| `U.Work` | one dated issue run in one workspace |
| `U.EvidenceRole` | checks, logs, screenshots, run record, workpad |

## Promise vs ability vs performance

- **promise**: what the harness claims it will deliver
- **ability**: what the harness can actually do under its work scope
- **performance**: measured outcomes such as cycle time, reopen rate, or defect escape rate

## Starter role assignments

- `Operator#ProductSteward:Ctx.ProjectDelivery.<name>`
- `DeliveryHarness.Cli#Orchestrator:Ctx.ProjectDelivery.<name>`
- `ConfiguredAgent#CodingAgent:Ctx.ProjectDelivery.<name>`
- `Reviewer#HumanReview:Ctx.ProjectDelivery.<name>`

## A.6.C boundary unpacking

- **L** laws/invariants
  - workspace path stays inside workspace root
  - repo docs are the system of record
  - target product and harness stay context-separated

- **A** admissibility/gates
  - only active issues are runnable
  - validation commands must be present
  - terminal workspaces can be cleaned only by policy

- **D** duties/commitments
  - reproduce first
  - do not expand scope silently
  - leave evidence
  - update docs when architecture changes

- **E** evidence
  - run record JSON
  - agent request file
  - test/build output
  - issue acceptance/validation notes

## FPF IDs used

- `A.1.1`
- `A.2.1`
- `A.2.2`
- `A.2.3`
- `A.15`
- `A.15.1`
- `A.15.2`
- `A.6.C`
- `F.9`
- `F.11`
- `F.17`
