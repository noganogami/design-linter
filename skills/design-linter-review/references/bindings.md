# Saved bindings (schemaVersion 1)

The parser version implementing this contract is 0.3.0. These files belong to design-linter, not to cc-sdd's standard outputs. No LLM is called by the CLI.

## Obtain current inputs

Run the ordinary CLI with `--format json`. Copy its `inputFingerprint` object as `inputs` in the binding file. It contains SHA-256 hashes of the decoded UTF-8 requirements and design strings, plus parser/profile/source revision identifiers. Paths are not hashed, so moving identical documents does not invalidate the binding. Line-ending/content changes do invalidate it.

The initial implementation uses whole-document fingerprints: even unrelated edits require review. Fine-grained invalidation is not implemented. This conservative behavior prevents silent reuse against changed context.

## JSON shape

```json
{
  "schemaVersion": 1,
  "inputs": {
    "requirements": "copy-from-report",
    "design": "copy-from-report",
    "parserVersion": "0.3.0",
    "profile": "cc-sdd-english-v1",
    "sourceRevision": "e2a0c671aef37a482404905b409ab5c27092b25b"
  },
  "entries": [
    {
      "id": "retry-interface",
      "kind": "referenceAlias",
      "from": "interface:Retry API",
      "to": "interface:PaymentRetryWorker Service Interface",
      "state": "proposed",
      "author": "design-linter-review",
      "rationale": "Explain the source-backed interpretation, not just similarity of names.",
      "evidence": [
        { "document": "design", "quote": "| 1.1–1.2 | Retry API |" },
        { "document": "design", "quote": "##### Service Interface" }
      ]
    }
  ]
}
```

All shown fields are required; unknown/duplicate property names are rejected. At least one entry is required. `id` values must be unique. `author`, `rationale`, `from` and `to` must be nonempty. `state` is `proposed` or `accepted`; this records interpretation state, not a proven fact. The CLI cannot authenticate who accepted an interpretation.

`evidence` is a nonempty list. Each quote must occur exactly once, verbatim, in `requirements` or `design`. Include full source lines covering the parser's evidence range, including a trailing line break if the reported endLine includes it. Read the `reviewIssues[].excerpts` and actual source context to find appropriate quotes. More context is necessary if a quote repeats. The CLI checks textual grounding, not semantic sufficiency of the rationale.

## Supported kinds

### referenceAlias

- `from` is an existing unresolved `realizedBy` target ID in `designIR.relations`.
- `to` is an existing entity ID of the same kind. E.g. `interface:X` can map to `interface:Y`, not to `component:Y`.
- Evidence must cover every source occurrence of that unresolved target and at least one target definition.
- One alias per source target; no chains, conflicting aliases, or replacing already resolved relationships.
- It applies to all occurrences of that target in this input. If different occurrences have different meanings, leave them unresolved; scoped bindings are not yet supported.

### fileMapping

- `from` is an existing `component:Name`.
- `to` is the exact normalized path of an existing concrete file in `designIR.files`.
- Evidence must cover a component definition and a file definition.
- Adds one explicit mapping to the candidate model. Duplicate mappings are rejected. Multiple distinct files per component are allowed.
- Does not create files, infer path bases, rename components, or suppress unsupported file-plan diagnostics.

No kind can ignore a finding, reinterpret free text as a note, introduce entities, or change dependencies. Unresolved cases outside these operations stay in the review.

## Results and process status

The regular `status`, `checks`, `designIR`, semantic diff and AI gate always describe the source documents without bindings. `supplementalReview` is separate:

| status | Meaning |
|---|---|
| CONDITIONAL | All entries were structurally valid; `mechanicalStatus`, `checks`, `designIR` describe the candidate under these interpretations |
| INVALID | Invalid schema version, reference, quote, conflicting entry or unsupported operation; no entries applied |
| STALE | Input fingerprints or parser/profile revision differ; no entries applied |

All entries are applied atomically. Even `accepted` entries produce CONDITIONAL output. Meaning is not verified merely because the candidate checks pass. Only ambiguity diagnostics fully resolved by an alias are retired in the candidate; unrelated uncertainty remains. Candidate relations retain original and binding evidence, and the supplied definitions are included in the output.

Exit precedence with bindings: INVALID/input errors → 2; STALE → 3; otherwise a raw FAIL → 1; any other binding review → 3. Without bindings, existing exit codes remain: PASS 0, FAIL 1, input error 2, UNKNOWN/REVIEW_REQUIRED 3. Do not treat exit 3 as an execution failure requiring repeated retries.

The tool reads files and writes stdout/stderr only. It does not edit the documents, update fingerprints, persist acceptance, or prompt for input. Save the binding file separately and pass it explicitly on subsequent runs.
