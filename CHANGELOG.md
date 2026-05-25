# Changelog

All notable changes to `Tamp.AxeCore` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] — Unreleased

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
