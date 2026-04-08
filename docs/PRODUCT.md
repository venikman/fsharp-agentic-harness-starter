# Product

## System name

DeliveryHarness

## Product type

service

## Core user outcome

Turn an eligible engineering issue into an isolated, evidence-backed agent run that a human can review and land safely.

## In scope

- repo-owned workflow contract in `WORKFLOW.md`
- deterministic per-issue workspaces under a configured workspace root
- issue intake from pluggable trackers, with file-backed intake first
- workspace lifecycle hooks for bootstrap, validation prep, and cleanup
- external coding-agent execution with bounded runtime settings
- run records, evidence paths, and handoff artifacts
- local-first operation for trusted environments while the harness matures

## Out of scope

- business logic for an unrelated target product
- a multi-tenant SaaS control plane or hosted orchestration service
- fully automated merge/landing of sensitive changes without human review
- arbitrary writes outside the assigned workspace root
- tracker-specific write automation as a hard requirement of the core orchestrator

## Critical invariants

- repo docs are the system of record for policy, workflow, and scope
- runtime configuration is harness-owned: `WORKFLOW.md`, repo docs, issue files, and harness-controlled environment variables are authoritative; pi must not become a second configuration surface
- `Ctx.TargetProduct` and `Ctx.ProjectDelivery` stay separate even when this repo evolves the harness itself
- coding agents run only inside sanitized per-issue workspaces under the configured workspace root
- workspaces are disposable execution environments, not the system of record
- every claimed success leaves validation evidence or explicit blocker evidence
- the default trust posture is local and explicit; tighter deployment controls are documented, not implied
