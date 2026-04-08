# Quality

This repository is a harness product first. Every review-ready change must leave evidence that the harness still loads its workflow, sees issues, and can execute at least the local dry-run path unless the issue explicitly changes that baseline.

## Minimum required checks

| Check | Command | Required | Evidence |
|---|---|---:|---|
| restore | `dotnet restore src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj` | yes | console output |
| build | `dotnet build src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj --no-restore` | yes | console output |
| workflow validation | `dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- validate-workflow` | yes | console output |
| issue listing | `dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- list-issues` | yes | console output |
| issue-run smoke | `dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- run-issue ACTIVE_ISSUE_ID` | when agent/workspace/orchestration behavior changes | console output and generated `.harness/runs/*.json` path |

- Use `list-issues` to choose an id currently in `tracker.active_states`; `run-issue` rejects non-active issues.

## Evidence rules

- Every issue must cite the exact validation commands it ran.
- Include relevant generated artifact paths such as workspace `.harness/*` files or run records under `.harness/runs/`.
- If orchestration behavior changes, include a short before/after note for operator-visible behavior.
- If failure-path behavior changes, include either a failed run record path or a bounded local smoke run showing what artifacts were preserved.
- If worker-command behavior changes, include a bounded local smoke run with generated request, transcript, and run-record paths.
- If host-mode, reload, or observability behavior changes, include the relevant `.harness/runtime/status.json` and/or `.harness/runtime/host-events.jsonl` artifact paths from a bounded local `serve` smoke.
- If workflow/config contracts change, update the matching docs in the same run.
- If real tracker or real agent checks cannot run because credentials or tooling are unavailable, record that explicitly as a blocker or skipped validation.

## Release bar

- acceptance criteria are satisfied
- required validations passed or were explicitly blocked with evidence
- changed file paths are listed in handoff notes
- open risks are called out clearly
- rollback or recovery guidance is included for tracker, agent, workflow, or workspace changes
