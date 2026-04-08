# Markdown parser tracker issue pack

This directory contains a **ready-to-copy `tracker/issues/` pack** for a separate F# markdown parser repository.

It is the file-backed form of the planning document at:

- `docs/examples/markdown-parser-library-backlog.md`

## Purpose

Use this when you want the delivery harness to orchestrate work for:

- `Ctx.TargetProduct.MarkdownParser`
- `Ctx.ProjectDelivery.MarkdownParserRepo`

Do **not** copy these issues into this harness repo's active `tracker/issues/`, because that would mix target-product work into `Ctx.ProjectDelivery.DeliveryHarnessRepo`.

## Contents

- `tracker/issues/README.md`
- `tracker/issues/MDP-0001.md` through `tracker/issues/MDP-0010.md`

## How to use

1. Create the target markdown-parser repo.
2. Copy `docs/examples/markdown-parser-issue-pack/tracker/issues/` into that repo as `tracker/issues/`.
3. Set the target repo workflow to use the file-backed tracker with the copied issue directory.
4. Replace illustrative validation commands if the target repo paths differ.
5. Add matching execution plans under `docs/exec-plans/active/` if you want the same bounded-plan discipline.

## Suggested copy command

```bash
mkdir -p /path/to/markdown-parser-repo/tracker
cp -R docs/examples/markdown-parser-issue-pack/tracker/issues /path/to/markdown-parser-repo/tracker/
```

## Assumptions baked into the pack

- issue ids use the `MDP` project key
- active work starts in `Todo`
- the target repo will have:
  - `src/MarkdownParser/MarkdownParser.fsproj`
  - `src/MarkdownParser.Cli/MarkdownParser.Cli.fsproj`
  - `docs/DIALECT.md`
  - `docs/AST.md`
  - `docs/reference-cases/`
  - `samples/`

Adjust those paths if the real repo shape differs.
