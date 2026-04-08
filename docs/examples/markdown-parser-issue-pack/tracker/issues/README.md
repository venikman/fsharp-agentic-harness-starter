# Markdown parser issue backlog

This directory is a copy-ready `tracker/issues/` pack for a separate markdown parser repository.

## Rules

- Issue files are the source of truth for file-backed intake.
- This `README.md` is documentation only and is ignored by the file tracker.
- Keep target-product work in the target repo, not in the harness repo backlog.
- The starter runtime reads `id`, `title`, `state`, `priority`, `acceptance`, `validation`, `constraints`, and the body text.
- `depends_on` and `fpf.*` fields are planning metadata that help ordering and review even though the starter runtime does not enforce them.

## Suggested rollout order

1. `MDP-0001` — define dialect boundary and non-goals
2. `MDP-0002` — lock the AST, spans, and public parse API
3. `MDP-0003` — establish normalization and block foundation
4. `MDP-0004`, `MDP-0005`, `MDP-0006` — add first useful block and inline forms
5. `MDP-0007`, `MDP-0008` — add links/images and nested block structure
6. `MDP-0009` — add renderer and CLI evidence surface
7. `MDP-0010` — publish reference cases and package the initial release

## Authoring reminders

- Keep acceptance measurable.
- Keep validation commands exact and local-first.
- Keep dialect boundaries explicit instead of implied.
- Update the matching docs when the parser semantics change.
- Prefer real sample inputs for evidence over synthetic claims of coverage.
