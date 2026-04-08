# PR: finalize harness runtime, optional Linear adapter, and release proof

## Summary

This PR closes the starter-harness backlog and proves the trusted-local runtime end to end.

It lands:
- long-running host mode (`serve`) and runtime status (`status`)
- retry/backoff and reconciliation in host mode
- structured host logs and JSON runtime snapshots
- workflow hot reload with last-known-good behavior
- explicit attempt/turn semantics
- strict prompt templating with compatibility mode
- read-only Linear-compatible tracker adapter behind the existing tracker seam
- final trusted-local release-proof docs/checklist updates
- archived execution plans copied to `docs/exec-plans/completed/` and active plans reduced to small references

## Why

The repo already had the core starter seams, but the final harness experience still needed:
- a real long-running scheduler/runner
- an honest workflow/runtime contract
- optional real tracker intake
- final release-proof evidence and documentation

## Main changes

### Runtime
- `src/DeliveryHarness.Cli/Program.fs`
- `src/DeliveryHarness.Core/Orchestrator.fs`
- `src/DeliveryHarness.Core/Observability.fs`
- `src/DeliveryHarness.Core/PromptTemplate.fs`
- `src/DeliveryHarness.Core/LinearTracker.fs`
- `src/DeliveryHarness.Core/Tracker.fs`
- `src/DeliveryHarness.Core/Workflow.fs`
- `src/DeliveryHarness.Core/Types.fs`
- `src/DeliveryHarness.Core/ProcessRunner.fs`
- `src/DeliveryHarness.Core/Agent.fs`

### Docs / repo memory
- `README.md`
- `WORKFLOW.md`
- `docs/ARCHITECTURE.md`
- `docs/QUALITY.md`
- `docs/SECURITY.md`
- `docs/exec-plans/active/*`
- `docs/exec-plans/completed/*`
- `tracker/issues/*`
- `docs/releases/v0.1.0.md`

## Validation

- `dotnet restore src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj`
- `dotnet build src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj --no-restore`
- `dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- validate-workflow`
- `dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- list-issues`
- `dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- run-issue DEMO-0009`
- bounded `dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- serve`
- `dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- status`

## Evidence

- `.harness/runs/20260408T120241418Z-DEMO-0009.json`
- `.harness/runs/20260408T122836999Z-DEMO-9999.json`
- `.harness/runtime/status.json`
- `.harness/runtime/host-events.jsonl`

## Remaining limits

- trusted-local only
- Linear adapter is read-only
- no continuation-capable multi-turn worker runtime yet
