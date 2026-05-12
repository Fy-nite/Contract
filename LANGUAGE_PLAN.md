# Contract language — v1 Roadmap and Implementation Plan

This document outlines concrete plans to evolve the Contract language tooling at three different levels. It gives options, estimated effort, deliverables, milestones, risks, and recommended next steps so we can pick a pragmatic path forward.

## Goals

- Ship stable v1 language semantics and tooling useful for editor integration, testing, and the reference VM.
- Provide precise diagnostics and editor features (diagnostic spans, hover signatures, completions) quickly.
- Maintain a single canonical implementation where practical (the C compiler/runtime) while allowing pragmatic shortcuts for editor tooling.

---

## Options overview

A — Near-term: Grammar + parser generator (recommended for editor UX)

- What: Convert the EBNF in `LANGUAGE_SPEC_v1.md` into a PEG or nearley grammar and generate a JS parser used by the LSP server. The parser will produce ASTs and diagnostics with precise start/end spans.
- Why: Fastest path to high-quality editor diagnostics and features. Keeps the C compiler as a runtime/IL emitter and uses the JS parser for LSP workflows.
- Deliverables:
  - `lsp/grammar/contract.ne` (nearley grammar) + generated parser files
  - `lsp/parser.js` wrapper that returns JSON AST + diagnostics for a given document
  - Updated `lsp/server.js` to prefer `lsp/parser.js` for diagnostics & symbols
  - Tests under `tests/` verifying parser diagnostics and AST shape
- Timeline: ~1–2 days (prototype) — adjust depending on grammar edge cases.
- Risks:
  - Duplicate parsing logic (JS + C) requires periodic sync.
  - Adds Node dependency for parsing; acceptable because LSP already runs in Node.

B — Medium-term: Improve the native C parser (canonical path)

- What: Expand `src/compiler.c` to a robust recursive-descent parser with full precedence, proper AST node ranges, block scoping, and clearer symbol tables. The compiler will emit debug info mapping bytecode back to source spans.
- Why: Single, canonical parser for compiler, IL emitter, and tooling that avoids duplication.
- Deliverables:
  - `src/parser.h` / `src/parser.c` refactor with parser modules and AST with start/end positions
  - Update `diagnose_source_str` to use AST ranges and richer diagnostics
  - Emission of debug/source-mapping into the bytecode (optional) and tests
- Timeline: ~3–7 days depending on desired completeness.
- Risks:
  - More C-level engineering; slower iteration and potential for regressions.
  - Larger changes to the build and test cycles.

C — Test-first: Conformance test-suite and CI (stabilize semantics)

- What: Build a comprehensive test-suite of positive/negative `.ct` examples and expected diagnostics/IL results, plus scripts and a lightweight comparison harness.
- Why: Provides a safety net for evolving the parser/VM and is low-risk to implement.
- Deliverables:
  - `tests/` expanded with `good/` and `bad/` cases and expected `.diag.json` or `.il` outputs
  - `tests/run_tests.sh` / `ci/check.sh` that run the suite and assert results
  - Optionally wire a CI job (GitHub Actions) that runs the test suite on each PR
- Timeline: ~1–2 days for a good starter suite.
- Risks: none significant; this is a strong enabler for either A or B.

---

## Recommendation

If your priority is editor UX (fast precise diagnostics + hover/completions), choose A (nearley/PEG parser in the LSP). It gives the best value quickly and integrates well with the existing Node LSP server. If you instead want a single canonical implementation in C and have time, choose B. In either case, adding C (conformance tests) is highly recommended before major refactors.

My suggested execution plan if you pick A now:

1. Convert EBNF -> nearley grammar and implement `lsp/parser.js`.
2. Wire `lsp/server.js` to call the parser for diagnostics/symbols (fall back to `build/Contract` if the parser throws or for IL generation).
3. Add tests in `tests/` that assert parser diagnostics for known errors (e.g., missing paren, unmatched braces, type in header, etc.).
4. Iterate until diagnostics are stable and editor highlights are precise.

Milestones & Targets (A)
- M1 (day 1 morning): Nearley grammar draft; parse simple files (functions, contracts, calls).
- M2 (day 1 afternoon): Diagnostic generation with precise spans; integrate into LSP diagnostics publishing.
- M3 (day 2): Symbol extraction (function signatures), completions, hover support; tests green.

Success criteria
- LSP publishes precise start/end diagnostic ranges for all samples in `tests/`.
- Hover shows function signatures for functions in the document and imported files.
- The C compiler remains the canonical emitter and is not required for editor diagnostics (but still used for build/run).

---

## Next steps

Reply with your choice: A, B or C (or a combination like A+C). Once you pick, I'll:

- expand the todo list with fine-grained tasks,
- implement the first milestone and run the test-suite,
- open a change with the updated artifacts and show how to run them locally.

If you want A (nearley), I’ll start converting the EBNF to a nearley grammar immediately.
