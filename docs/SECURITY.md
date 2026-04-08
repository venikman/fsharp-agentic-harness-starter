# Security

## Trust model

This starter assumes a trusted local environment while the harness is being proven.
Tighten this before using unattended remote execution.

## Rules

- Do not commit secrets.
- Read secrets from environment variables when needed.
- For `tracker.kind: linear`, keep `tracker.api_key` as an environment-variable reference such as `$LINEAR_API_KEY`; do not inline tracker credentials in repo-owned files.
- Prefer whole-token environment-variable references in `WORKFLOW.md` for secret-bearing command arguments.
- Secret-like env values referenced that way are redacted from operator-visible summaries and default transcripts, but prompt text and issue bodies must still stay secret-free.
- Do not allow the agent to write outside the assigned workspace.
- Do not auto-merge security-sensitive changes without human review.
- Networked side effects must be explicitly allowed by the issue.
- The current Linear tracker adapter is read-only; tracker comments, transitions, and other writes remain outside the orchestrator core in this pass.

## Escalate to human review for

- credential handling
- infrastructure access changes
- authn/authz changes
- production database changes
- billing or legal surfaces
