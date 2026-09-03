# Contributing to Contract

Thanks for wanting to contribute. This guide covers how to set up your
environment, how to make changes that fit the project, and how to keep the
changelog accurate.

## Getting started

### Prerequisites

- [.NET SDK 10.x](https://dotnet.microsoft.com/download)
- Git with submodule support

### Clone (with submodules)

The runtime (`libs/Objekt-RT`) and core libs are Git submodules. Clone with
`--recursive` or run `git submodule update --init --recursive` if you already
have a checkout.

```bash
git clone https://github.com/fy-nite/contract --recursive
cd contract
dotnet build
```

## Making a change

1. Fork the repository.
2. Create a feature branch:
   ```bash
   git checkout -b feature/amazing-feature
   ```
3. Make your changes.
4. For new features, add a test contract:
   - Put a program that **must compile** in `tests/success/`.
   - Put a program that **must fail** in `tests/failure/`.
   - Add examples under `scratch/` or `examples/` when they help show a
     workflow rather than a single feature.
5. Write or update documentation under `docs/` (language changes should update
   `docs/CONTRACT_LANGUAGE.md`, and spec changes `docs/CONTRACT_SPEC.typ`).
6. Run the checks below.
7. Commit, push, and open a Pull Request.

## Running the checks

The compiler ships an in-process test suite:

```bash
ccl --test
```

or, without installing the CLI first, against the built binary:

```bash
dotnet run --project Contract.Cli -- --test
```

CI runs on every push and pull request against `main` and executes:

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build --verbosity normal
```

Make sure both the test suite and the build pass before requesting review.

## Commit messages

Write commit messages that describe *what* and *why*, following the project's
style. Prefix with a conventional type when it helps (`feat:`, `fix:`,
`chore:`, `docs:`), e.g.:

```text
Implement indexers in the contract language
```

```text
feat: is/null safety ops, try/catch tests, VM perf
```

## Keeping the changelog

The changelog lives in `CHANGELOG.md` and follows the
[Keep a Changelog](https://keepachangelog.com) style. Every pull request that
changes user-facing behavior (a language feature, a CLI command or flag, a
runtime/interop behavior, build tooling, or docs that users rely on) must be
reflected in it.

### The rules

1. **Update `CHANGELOG.md` in the same PR as the change.** Never let the
   changelog drift behind the code; it is easier to summarize a feature while
   it is fresh than to reconstruct it later.
2. **Unreleased section only.** Add your entry under `## [Unreleased]` — do not
   create a versioned heading. Versions are cut at release time by whoever is
   doing the release.
3. **One entry per user-visible change**, placed under the matching subsection:
   - `### Added` — new features, functions, commands, flags, docs.
   - `### Changed` — behavior or format changes, enhanced/updated existing
     behavior.
   - `### Deprecated` — things being phased out.
   - `### Removed` — features or references that were dropped.
   - `### Fixed` — bug fixes.
   - `### Security` — vulnerability fixes.
4. **New entries go at the top of their subsection.**
5. **Write it in the present-ish, terse imperative** used throughout the file
   ("Add support for …", "Remove …", "Enhance …"). Keep each line to a short,
   specific summary. No issue/PR links required.
6. **Do not log** dependency bumps, whitespace, renaming a PRIVATE identifier,
   or pure internal refactors that change nothing user-visible. If in doubt,
   lean toward adding it — it is better to over-log a real change than to
   forget one.
7. If a PR bundles several related changes, group them under one concise entry
   in the most relevant subsection rather than spraying fragments.

### Releasing (for maintainers)

At release time:

1. Decide a version number.
2. Rename the `## [Unreleased]` heading to that version, e.g.
   `## [1.4.0]`, with a date: `## [1.4.0] - 2026-09-03`.
3. Start a fresh empty `## [Unreleased]` section on top.
4. (Optional) record the release commit in a `[Unreleased]:` / versioned
   `[...]:` comparison URL block if the project tracks those.
5. Capture the current `HEAD` hash so the next release can diff from it —
   that hash is the "last release" anchor used to build the next changelog.

### Building the changelog from git

To assemble the entries between two points (the last release and now):

```bash
git log <last-release>..HEAD --stat
```

`git log --oneline <last-release>..HEAD` gives the list of commits, and
`--stat` shows which files each touched so you can tell whether a commit is a
user-visible feature, a bug fix, or internal churn. Group them into the
subsections above and dedupe.
