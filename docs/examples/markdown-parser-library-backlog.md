# Markdown parser library backlog pack for harness orchestration

## Purpose

This file is a **target-product planning pack**, not the active backlog of `Ctx.ProjectDelivery.DeliveryHarnessRepo`.

Use it when you want this harness to orchestrate work for a separate target product:

- `Ctx.TargetProduct.MarkdownParser`
- `Ctx.ProjectDelivery.MarkdownParserRepo`

That separation follows **FPF A.1.1**: the target product context must stay distinct from the delivery-harness context.

## FPF basis used

This issue list is shaped primarily by:

- **A.1.1 `U.BoundedContext`**
  - keep the markdown parser product context separate from the harness repo context
- **A.6.C Contract Unpacking for Boundaries**
  - each issue carries explicit acceptance, validation, and constraints instead of contract soup
- **A.15.2 `U.WorkPlan`**
  - the ordered dependency list is the bounded work plan
- **B.5.1 Explore → Shape → Evidence → Operate**
  - issues are grouped by lifecycle state, not just by implementation detail
- **F.11 Method Quartet Harmonisation**
  - keep grammar/design description, implementation work, and execution evidence separate

## Assumed target-repo shape

Adapt these paths once the real target repo exists.

```text
.
├── WORKFLOW.md
├── tracker/issues/
├── src/
│   ├── MarkdownParser/
│   │   └── MarkdownParser.fsproj
│   └── MarkdownParser.Cli/
│       └── MarkdownParser.Cli.fsproj
├── docs/
│   ├── DIALECT.md
│   ├── AST.md
│   └── reference-cases/
└── samples/
```

## Global admissibility gates

Before running any issue in the target repo, keep these global gates true:

- `WORKFLOW.md` remains the authoritative runtime contract.
- The markdown parser stays a **local-first** product build; no network access is required for core parsing work.
- Issue files must keep **acceptance**, **validation**, and **constraints** explicit.
- Validation should prefer **builds and smoke runs with real sample inputs**, not synthetic harness-only proving surfaces.
- Any dialect expansion must update the dialect boundary doc before broad implementation continues.

## Ordered issue list

| ID | Lifecycle | Title | Depends on |
|---|---|---|---|
| MDP-0001 | Exploration | Define markdown parser product boundary and supported dialect slice | — |
| MDP-0002 | Shaping | Define AST, spans, parse result model, and public library API | MDP-0001 |
| MDP-0003 | Shaping | Implement source normalization and block parsing foundation | MDP-0002 |
| MDP-0004 | Shaping | Parse paragraphs, headings, and thematic breaks | MDP-0003 |
| MDP-0005 | Shaping | Parse code spans, indented code blocks, and fenced code blocks | MDP-0003 |
| MDP-0006 | Shaping | Parse emphasis and strong emphasis with explicit delimiter rules | MDP-0003 |
| MDP-0007 | Shaping | Parse links, images, and reference definitions | MDP-0006 |
| MDP-0008 | Shaping | Parse lists and block quotes with nested structure | MDP-0004, MDP-0005, MDP-0006 |
| MDP-0009 | Evidence | Add HTML renderer and CLI smoke surface for real sample inputs | MDP-0007, MDP-0008 |
| MDP-0010 | Evidence | Publish reference-case evidence and package the library for initial release | MDP-0009 |

---

## Issue pack

### MDP-0001 — Define markdown parser product boundary and supported dialect slice

- lifecycle: `Exploration`
- priority: `1`
- depends_on: none
- fpf.primary_patterns:
  - `A.1.1`
  - `A.6.C`
  - `B.5.1`
  - `F.11`

**Acceptance**
- `docs/DIALECT.md` names the initial supported markdown slice and explicit non-goals.
- The repo states whether it targets CommonMark, a subset, or a repo-specific dialect.
- The boundary distinguishes parsing, rendering, and CLI/operator concerns.

**Validation**
- `dotnet build src/MarkdownParser/MarkdownParser.fsproj`
- `dotnet build src/MarkdownParser.Cli/MarkdownParser.Cli.fsproj`

**Constraints**
- Do not silently claim full CommonMark coverage.
- Keep target-product scope separate from harness/runtime concerns.
- Prefer the smallest initial dialect that can grow cleanly.

### MDP-0002 — Define AST, spans, parse result model, and public library API

- lifecycle: `Shaping`
- priority: `1`
- depends_on:
  - `MDP-0001`
- fpf.primary_patterns:
  - `A.15.2`
  - `A.6.C`
  - `F.11`

**Acceptance**
- `docs/AST.md` defines the node model, source-span model, and parse-result/error surface.
- The library exposes a small public entry point for parsing markdown text into the AST.
- The API separates parser result data from CLI formatting concerns.

**Validation**
- `dotnet build src/MarkdownParser/MarkdownParser.fsproj`
- `dotnet run --project src/MarkdownParser.Cli/MarkdownParser.Cli.fsproj -- --help`

**Constraints**
- Keep the AST stable and boring before feature growth.
- Do not mix rendering-only concerns into the parse tree unless justified in `docs/AST.md`.

### MDP-0003 — Implement source normalization and block parsing foundation

- lifecycle: `Shaping`
- priority: `1`
- depends_on:
  - `MDP-0002`
- fpf.primary_patterns:
  - `A.15.2`
  - `B.5.1`
  - `F.11`

**Acceptance**
- The parser normalizes line endings and source positions consistently.
- A block-parsing foundation exists for scanning lines into block-level parse decisions.
- Source spans remain stable through normalization.

**Validation**
- `dotnet build src/MarkdownParser/MarkdownParser.fsproj`
- `dotnet run --project src/MarkdownParser.Cli/MarkdownParser.Cli.fsproj -- samples/basic-lines.md`

**Constraints**
- Keep normalization rules explicit in docs or code comments.
- Do not start inline-feature work before block foundations are stable.

### MDP-0004 — Parse paragraphs, headings, and thematic breaks

- lifecycle: `Shaping`
- priority: `1`
- depends_on:
  - `MDP-0003`
- fpf.primary_patterns:
  - `A.15.2`
  - `B.5.1`

**Acceptance**
- Paragraphs parse into stable AST nodes.
- ATX headings, setext headings, and thematic breaks are supported according to the chosen dialect boundary.
- The CLI smoke output makes heading levels and block boundaries inspectable.

**Validation**
- `dotnet build src/MarkdownParser/MarkdownParser.fsproj`
- `dotnet run --project src/MarkdownParser.Cli/MarkdownParser.Cli.fsproj -- samples/headings-and-breaks.md`

**Constraints**
- Stay inside the dialect slice declared in `docs/DIALECT.md`.
- Prefer explicit unsupported-case notes over ambiguous parsing.

### MDP-0005 — Parse code spans, indented code blocks, and fenced code blocks

- lifecycle: `Shaping`
- priority: `2`
- depends_on:
  - `MDP-0003`
- fpf.primary_patterns:
  - `A.15.2`
  - `B.5.1`

**Acceptance**
- Inline code spans parse with preserved raw text semantics expected by the chosen dialect.
- Indented code blocks and fenced code blocks parse as distinct node forms when the dialect distinguishes them.
- Fence info strings, if supported, are surfaced explicitly in the AST.

**Validation**
- `dotnet build src/MarkdownParser/MarkdownParser.fsproj`
- `dotnet run --project src/MarkdownParser.Cli/MarkdownParser.Cli.fsproj -- samples/code-forms.md`

**Constraints**
- Do not overgeneralize raw text handling before the dialect policy is documented.
- Keep code-node source spans auditable.

### MDP-0006 — Parse emphasis and strong emphasis with explicit delimiter rules

- lifecycle: `Shaping`
- priority: `2`
- depends_on:
  - `MDP-0003`
- fpf.primary_patterns:
  - `A.15.2`
  - `B.5.1`
  - `F.11`

**Acceptance**
- Emphasis and strong emphasis parse according to documented delimiter rules.
- The chosen rules for `_` and `*` are written down explicitly instead of living only in code.
- Ambiguous delimiter cases either parse deterministically or are documented as out of scope for the initial slice.

**Validation**
- `dotnet build src/MarkdownParser/MarkdownParser.fsproj`
- `dotnet run --project src/MarkdownParser.Cli/MarkdownParser.Cli.fsproj -- samples/emphasis.md`

**Constraints**
- Keep delimiter semantics documented before widening coverage.
- Avoid silent behavior borrowed from an unstated external parser.

### MDP-0007 — Parse links, images, and reference definitions

- lifecycle: `Shaping`
- priority: `2`
- depends_on:
  - `MDP-0006`
- fpf.primary_patterns:
  - `A.15.2`
  - `A.6.C`
  - `B.5.1`

**Acceptance**
- Inline links and images parse into explicit AST forms.
- Reference definitions and reference-style links are either supported explicitly or marked out of scope in the dialect doc.
- URL/title/reference normalization rules are visible in docs or code comments.

**Validation**
- `dotnet build src/MarkdownParser/MarkdownParser.fsproj`
- `dotnet run --project src/MarkdownParser.Cli/MarkdownParser.Cli.fsproj -- samples/links-and-images.md`

**Constraints**
- Keep reference-definition semantics distinct from rendering semantics.
- Do not hide unsupported link cases behind lossy parsing.

### MDP-0008 — Parse lists and block quotes with nested structure

- lifecycle: `Shaping`
- priority: `2`
- depends_on:
  - `MDP-0004`
  - `MDP-0005`
  - `MDP-0006`
- fpf.primary_patterns:
  - `A.15.2`
  - `B.5.1`

**Acceptance**
- Ordered lists, unordered lists, and block quotes parse into nested AST structure.
- Nesting behavior is deterministic and documented for the chosen dialect slice.
- The CLI smoke output shows nested structure clearly enough for review.

**Validation**
- `dotnet build src/MarkdownParser/MarkdownParser.fsproj`
- `dotnet run --project src/MarkdownParser.Cli/MarkdownParser.Cli.fsproj -- samples/lists-and-quotes.md`

**Constraints**
- Keep nesting rules explicit.
- Avoid mixing renderer heuristics into parser structure decisions.

### MDP-0009 — Add HTML renderer and CLI smoke surface for real sample inputs

- lifecycle: `Evidence`
- priority: `3`
- depends_on:
  - `MDP-0007`
  - `MDP-0008`
- fpf.primary_patterns:
  - `A.6.C`
  - `A.15.2`
  - `B.5.1`
  - `F.11`

**Acceptance**
- The library can render the supported AST subset to HTML.
- Escaping policy is explicit and safe for the chosen initial release scope.
- The CLI can print either AST output or rendered HTML for real sample inputs.

**Validation**
- `dotnet build src/MarkdownParser/MarkdownParser.fsproj`
- `dotnet run --project src/MarkdownParser.Cli/MarkdownParser.Cli.fsproj -- --format ast samples/showcase.md`
- `dotnet run --project src/MarkdownParser.Cli/MarkdownParser.Cli.fsproj -- --format html samples/showcase.md`

**Constraints**
- Keep renderer policy explicit about raw HTML handling.
- Do not claim sanitizer behavior unless it is actually implemented and documented.

### MDP-0010 — Publish reference-case evidence and package the library for initial release

- lifecycle: `Evidence`
- priority: `3`
- depends_on:
  - `MDP-0009`
- fpf.primary_patterns:
  - `A.6.C`
  - `A.15.2`
  - `B.5.1`

**Acceptance**
- `docs/reference-cases/` records representative input/output cases for the supported dialect slice.
- The repo documents the exact supported feature set, known exclusions, and release usage examples.
- The library builds in release mode and the CLI smoke surface remains usable for evidence gathering.

**Validation**
- `dotnet build src/MarkdownParser/MarkdownParser.fsproj -c Release`
- `dotnet run --project src/MarkdownParser.Cli/MarkdownParser.Cli.fsproj -- --format html samples/showcase.md`
- `dotnet run --project src/MarkdownParser.Cli/MarkdownParser.Cli.fsproj -- --format ast samples/showcase.md`

**Constraints**
- Evidence should use real reference cases for the supported dialect, not placeholder claims of coverage.
- Release docs must describe only implemented behavior.

---

## How to use this pack with the harness

1. Create the target repo for the markdown parser library.
2. Copy the relevant issues from this pack into that repo’s `tracker/issues/`.
3. Replace the illustrative validation commands with the exact commands of the target repo.
4. Keep the harness repo’s own backlog separate from the markdown parser backlog.
5. Run the harness against the target repo, not against `Ctx.ProjectDelivery.DeliveryHarnessRepo`, when doing target-product work.

## Why this pack is harness-friendly

- The issue order is explicit.
- Dependencies are bounded and reviewable.
- Acceptance, validation, and constraints are written as separate surfaces.
- The phases follow **Explore → Shape → Evidence** from **B.5.1**.
- The library design work and execution evidence are kept distinct in the spirit of **F.11**.
