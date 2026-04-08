# AGENTS.md

This file is a map, not an encyclopedia.

## Read in this order

1. `docs/PRODUCT.md`
   - what the target product is
   - what is in scope and out of scope

2. `docs/ARCHITECTURE.md`
   - target-product boundaries
   - delivery-harness boundaries
   - module ownership and dependency rules

3. `docs/QUALITY.md`
   - required checks
   - evidence expectations
   - release bar

4. `docs/SECURITY.md`
   - trust model
   - network and secret handling rules
   - prohibited actions

5. `WORKFLOW.md`
   - tracker selection
   - workspace lifecycle
   - agent contract
   - unattended rules

6. `docs/exec-plans/active/`
   - current execution plans
   - bounded implementation steps

## Working rules

- Treat repo-local docs as the system of record.
- Do not invent architecture that is not documented.
- Do not expand scope beyond the issue.
- Work only inside the assigned workspace.
- Leave evidence, not just code.
- When uncertain, update docs before code.
