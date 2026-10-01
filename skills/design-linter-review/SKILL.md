---
name: design-linter-review
description: Run design-linter on cc-sdd requirements.md and design.md, explain grouped unresolved items, and propose persisted bindings for ambiguous references and file mappings. Use for mechanical design review and interpretation of its results.
---

# Design Linter Review

Run the deterministic CLI first, then explain what it verified and what remains unresolved. The CLI is noninteractive. Do not require every ambiguity to be resolved to deliver a useful review.

## Run

Use the user's available `design-linter` executable:

```sh
design-linter --requirements <requirements.md> --design <design.md> --format json
```

In the design-linter source repository, the development equivalent is `proto run dotnet -- run --project src/DesignLinter --` followed by the same arguments. If no executable or source checkout is available, ask for its location instead of inventing results.

Only pass `--before-requirements` and `--before-design` when both real comparison snapshots are available. cc-sdd does not produce special before-design files. Keep any reports and bindings separate from the input documents.

Exit 1 and 3 can contain a valid JSON review; inspect it. Exit 2 can contain an INVALID binding report or an input error on stderr.

## Interpret

- Read `checks`, `reviewIssues`, `semanticDiff`, and `limitations`. `reviewIssues` groups causes with source excerpts, target IDs, affected requirement IDs and rule IDs. Inspect the cited context in the input documents before proposing an interpretation.
- Distinguish unsupported syntax, ambiguous meaning, missing information, conflicts and unresolved references. UNKNOWN is not automatically a design flaw.
- Do not infer a dependency direction from a sequence diagram or a path base from suffix matching alone. Give source-backed alternatives when possible; say what fact is missing when no alternative can be justified.
- Prioritize confirmed failures, then decisions that resolve multiple affected checks. Present at most three main human questions with evidence and impact; preserve all other findings in the detailed report.

## Persist a candidate interpretation

For unresolved requirement target names or component-to-file mappings, read [references/bindings.md](references/bindings.md) for the exact supported contract. The current CLI does not support dependency bindings, note/ignore bindings, general path-base settings or arbitrary IR edits.

Write a separate JSON binding file with exact quotations, rationale, author and `state: proposed`. Reuse unchanged entries. Use `accepted` only when an existing user decision supports it; do not mark a proposal accepted because it passed structural validation.

Run the same CLI with `--bindings <file>` and inspect `supplementalReview`. The raw result remains unchanged; `CONDITIONAL` reports what holds under the saved interpretation. Report both results and the interpretation state. A matching quote or an existing target proves neither the intended meaning nor requirement satisfaction.

For INVALID, correct only errors supported by the source. For STALE, review the changed documents and affected interpretations before replacing fingerprints. Never refresh a fingerprint just to make validation pass. Stop retrying when no new evidence or meaningful correction is available and present the remaining decision to the user.

Do not require confirmation of every entry on every run. Ask only for material interpretations that remain unresolved. Document edits are an alternative when requested, not a prerequisite for using this skill.

## Review boundary

The skill can explain UNKNOWN and prepare bindings before mechanical PASS. This is interpretation support, not a completed semantic AI Review. If a separate quality review is requested, perform it only after the raw mechanical checks pass; a conditional PASS alone does not open that gate. Keep its important issues to at most three. Mechanical PASS is never design approval.
