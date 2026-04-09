---
id: DEMO-0000
title: Replace with issue title
state: Todo
priority: 10
depends_on:
  # File-backed tracker mode enforces these prerequisites for candidate admission and manual run dispatch.
  # - DEMO-0001
# Optional repo-delivery metadata below is not enforced by the starter runtime today:
# fpf.lifecycle_state: Evidence
# fpf.semantic_risk: low
# fpf.primary_patterns:
#   - A.15.2
acceptance:
  - Replace with measurable acceptance criteria
validation:
  - dotnet build src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj
  - dotnet run --project src/DeliveryHarness.Cli/DeliveryHarness.Cli.fsproj -- validate-workflow
constraints:
  - Replace with scope constraints
---
# Problem

Describe the user-visible or system-visible problem.

# Scope

Describe what is in scope.

# Notes

Execution plan:
- `docs/exec-plans/active/DEMO-0000.md`

Add architecture notes, links to docs, and known risks.

Runtime reminder:
- In file-backed mode, `depends_on` participates in runtime admission checks. `fpf.*` remains repo-delivery metadata only.
