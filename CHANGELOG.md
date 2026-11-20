# Changelog

All notable changes to `Tamp.AxeCore` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] - 2026-11-20 - .NET 11 in, .NET 8 and .NET 9 out

### Changed

- **Multi-targets `net10.0;net11.0`** instead of `net8.0;net9.0;net10.0` ([ADR 0015](https://github.com/tamp-build/tamp/blob/main/docs/adr/0015-target-framework-strategy.md)). .NET 11 shipped 2026-11-10 and .NET 8 and .NET 9 reached end of support the same day; the rule is to add a TFM the day a release ships and drop one the day Microsoft ends its support. `global.json` pins SDK 11.0.100.

  **If you target `net8.0` or `net9.0`, stay on the previous version** — this one carries no assets your project can resolve.

- **`Tamp.*` pinned to 1.21.0**, which carries a SARIF fix worth knowing about if this project's output is read as evidence: before 1.21.0, a SARIF result whose scanner omitted `level` was read as `none` — the least severe value — instead of the declared `warning`, on every runtime before net11 ([tamp#166](https://github.com/tamp-build/tamp/issues/166)).

- **Publishing moved to nuget.org trusted publishing (OIDC).** The `tamp_nuget_api_key` org secret expired 2026-11-01 along with every other pre-2026-08-17 nuget.org key. The publish job now mints a short-lived key from its own OIDC token, so there is no long-lived credential to rotate or leak.

## [0.1.2] — 2026-09-27

### Added

- **`AxeCoreSarif.AnnotateResults(sarifPath, params string[] tags)`** — post-process the SARIF file produced by `AxeCore.ConvertToSarif` to inject `properties.tags` entries on every result. Source-side tagging path that lets downstream SARIF consumers (`tamp.findings` `/ingest/findings`, DefectDojo, etc.) route by category without filename / scanner-name heuristics. Idempotent; preserves existing tags; case-sensitive dedup per SARIF 2.1.0 spec. No new dependency surface (uses `System.Text.Json.Nodes.JsonNode` from the BCL).
- 17 new tests for the annotator: happy path, multi-tag append, preserve-and-append on existing properties, idempotency, multi-run walk, defensive replacement for misshaped `tags` field, no-op on empty input, argument/file error paths.
- Package now ships XML documentation files (`.xml`) alongside the assembly, so consumers get IntelliSense and API docs. (Mirrors [tamp-build/tamp#3](https://github.com/tamp-build/tamp/pull/50).)

### Why

`@axe-core/cli` + `axe-sarif-converter` produce SARIF 2.1.0 but don't stamp a category property on each result. Downstream sinks consuming SARIF from multiple scanners (axe + opengrep + trivy + roslyn + ...) end up doing scanner-name string-matching to route findings to the right severity / sub-category bucket. Source-side tagging via `properties.tags` matches what Trivy emits natively and gives every sink a uniform extraction path. Requested by tamp.findings after TFND-27 adoption.

### Usage

```csharp
ProcessRunner.Execute(AxeCore.Scan(s => s.AddUrl(url).SetOutputFile(jsonPath)));
ProcessRunner.Execute(AxeCore.ConvertToSarif(s => s.SetInputFile(jsonPath).SetOutputFile(sarifPath)));
AxeCoreSarif.AnnotateResults(sarifPath, "accessibility");
```

## [0.1.0] — 2026-05-25

### Added

- Initial release. Two-verb Tamp wrapper for standalone web accessibility scanning:
  - `AxeCore.Scan(...)` — wraps `@axe-core/cli` (positional URLs + `--save`-driven JSON output).
  - `AxeCore.ConvertToSarif(...)` — wraps `axe-sarif-converter` (positional input JSON + output SARIF). Required because `@axe-core/cli` does not emit SARIF natively.
- `AxeCoreBinaryResolver` with smart resolution per binary: project-local `node_modules/.bin/<binary>` → `pnpm exec <binary>` (when `pnpm-lock.yaml` / `pnpm-workspace.yaml` present + `pnpm` on PATH) → `npm exec <binary>` (when `package-lock.json` present + `npm` on PATH) → global `<binary>` on PATH.
- `AxeCoreBinaryResolver.IsAvailable(workingDirectory)` returns true only when **both** the `axe` CLI and the `axe-sarif-converter` CLI resolve — pre-flight for skippable security-pipeline targets.
- `AxeCoreBinaryResolver.TryResolve(binary, workingDirectory)` generic resolver for arbitrary binary names in the same workspace.
- Scan settings cover the common CI surface: URLs, output, rules / tags scoping (`wcag2a` / `wcag2aa` / `wcag21aa` / `wcag22aa` / `best-practice`), include / exclude CSS selectors, browser selection, Chromium binary override, exit-on-violation (default true), timeout, load-delay, no-sandbox toggle, no-reporter toggle.
- Parallel fluent + object-init authoring surface on both verbs.
- Multi-target `net8.0;net9.0;net10.0`.

### Closes

- TAM-277 — Tamp.AxeCore satellite — standalone a11y URL scans via @axe-core/cli.
