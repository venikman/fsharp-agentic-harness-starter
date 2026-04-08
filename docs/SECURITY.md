# Security

## Trust model

This starter assumes a trusted local environment while the harness is being proven.
Tighten this before using unattended remote execution.

## Rules

- Do not commit secrets.
- Read secrets from environment variables when needed.
- Do not allow the agent to write outside the assigned workspace.
- Do not auto-merge security-sensitive changes without human review.
- Networked side effects must be explicitly allowed by the issue.

## Escalate to human review for

- credential handling
- infrastructure access changes
- authn/authz changes
- production database changes
- billing or legal surfaces
