# Tamp.AxeCore

> Tamp CommandPlan wrappers for standalone web accessibility scanning — `@axe-core/cli` for the scan itself and `axe-sarif-converter` for the JSON → SARIF emission that feeds the Tamp security pipeline.

| Package | Status |
|---|---|
| `Tamp.AxeCore` | 0.1.0 (initial) |

## Install

```bash
dotnet add package Tamp.AxeCore
```

Multi-targets net8 / net9 / net10. The wrapper is .NET; it shells out to two npm-distributed CLIs (`axe` and `axe-sarif-converter`) that live in your project's `node_modules` (or a global install). See [Binary resolution](#binary-resolution) below for the resolution order the wrapper uses.

## When to use this — and when NOT to

**Use `Tamp.AxeCore`** when you want to scan a **deployed URL** for accessibility violations from your build pipeline — typical for SaaS / public-facing apps where the security target probes a staging / preview environment after deploy.

**Don't use `Tamp.AxeCore`** if you already have Playwright tests. `@axe-core/playwright` is a JS-side library that runs `AxeBuilder({ page }).analyze()` from inside your existing test files — install via `Tamp.Npm` and the findings flow through your test results. No .NET wrapper layer needed (and no separate SARIF conversion step).

## Why two verbs

Unlike ESLint or Trivy, `@axe-core/cli` does **not** emit SARIF natively — it produces axe's own JSON shape. The Tamp security chain consumes SARIF, so a separate conversion step is needed. `Tamp.AxeCore` ships both verbs:

- `AxeCore.Scan(...)` — wraps `@axe-core/cli`, produces raw axe JSON.
- `AxeCore.ConvertToSarif(...)` — wraps `axe-sarif-converter`, converts that JSON to SARIF 2.1.0.

Both `axe` and `axe-sarif-converter` are separate npm packages — install both in your project's devDependencies:

```bash
pnpm add -D @axe-core/cli axe-sarif-converter
# or
npm install -D @axe-core/cli axe-sarif-converter
```

## Quick start — skippable security-pipeline target

```csharp
using Tamp;
using Tamp.AxeCore;

[Parameter] readonly string StagingUrl = "https://staging.example.com";
AbsolutePath WebRoot => RootDirectory / "web";
AbsolutePath SecurityArtifacts => RootDirectory / "artifacts" / "security";

Target SecurityScanAxeCore => _ => _
    .Description("Standalone a11y scan against the deployed staging URL.")
    .Executes(() =>
    {
        if (!AxeCoreBinaryResolver.IsAvailable(WebRoot))
        {
            Log.Info("[security] AxeCore skipped — toolchain not installed at {Dir}", WebRoot);
            return;
        }

        SecurityArtifacts.CreateDirectory();
        var json  = SecurityArtifacts / "axe.json";
        var sarif = SecurityArtifacts / "axe.sarif";

        var scanPlan = AxeCore.Scan(s => s
            .SetWorkingDirectory(WebRoot)
            .AddUrl(StagingUrl)
            .SetOutputFile(json)
            .AddTag("wcag2a").AddTag("wcag2aa").AddTag("wcag21aa").AddTag("best-practice")
            .SetBrowser("chromium")
            .SetNoSandbox()          // required for Docker/CI containers
            .SetTimeoutSeconds(60)
            .SetLoadDelayMs(2000));

        var scanExit = ProcessRunner.Execute(scanPlan);
        // axe exits 1 on violations when --exit is set; treat 0 and 1 as successful scan.
        if (scanExit > 1) throw new InvalidOperationException($"axe-core failed with exit {scanExit}.");

        var sarifPlan = AxeCore.ConvertToSarif(s => s
            .SetWorkingDirectory(WebRoot)
            .SetInputFile(json)
            .SetOutputFile(sarif));
        ProcessRunner.Execute(sarifPlan);

        // sarif is now ready for /ingest/findings (tamp-ingest-v1).
    });
```

## Verb surface (v1)

### `AxeCore.Scan` — `axe <urls...> --save <json> [flags]`

| Setting | CLI flag | Required |
|---|---|---|
| `AddUrl(url)` | positional (repeatable) | ≥1 |
| `SetOutputFile(json)` | `--save <path>` | yes |
| `AddRule(id)` (repeatable) | `--rules <id,id,...>` | no |
| `AddTag(tag)` (repeatable) | `--tags <tag,tag,...>` | no |
| `AddInclude(selector)` | `--include <selector>` (repeatable) | no |
| `AddExclude(selector)` | `--exclude <selector>` (repeatable) | no |
| `SetBrowser(name)` | `--browser <name>` | no |
| `SetChromiumBinary(path)` | `--chromium-binary <path>` | no |
| `SetExit(true)` (default) | `--exit` | — |
| `SetTimeoutSeconds(n)` | `--timeout <n>` | no |
| `SetLoadDelayMs(ms)` | `--load-delay <ms>` | no |
| `SetNoSandbox(true)` | `--no-sandbox` | no |
| `SetNoReporter(true)` | `--no-reporter` | no |

Common WCAG tags: `wcag2a`, `wcag2aa`, `wcag21aa`, `wcag22aa`, `best-practice`. Combine to scope the rule set.

### `AxeCore.ConvertToSarif` — `axe-sarif-converter <input.json> <output.sarif>`

| Setting | Position | Required |
|---|---|---|
| `SetInputFile(json)` | positional 1 | yes |
| `SetOutputFile(sarif)` | positional 2 | yes |

Pure conversion — no flags beyond the positional input/output. The output SARIF 2.1.0 plugs straight into the [tamp-ingest-v1](https://github.com/tamp-build/tamp-findings#tamp-ingest-v1) `/ingest/findings` contract.

## Binary resolution

Neither `axe` nor `axe-sarif-converter` are .NET tools — both ship as npm packages. The wrapper resolves them at `Scan(...)` / `ConvertToSarif(...)` time in priority order:

1. **Project-local** — `{WorkingDirectory}/node_modules/.bin/<binary>` (or `<binary>.cmd` on Windows). Preferred because the project's exact pinned versions run.
2. **pnpm exec** — `pnpm exec <binary> -- ...`. Used when `pnpm-lock.yaml` or `pnpm-workspace.yaml` is present AND `pnpm` is on `PATH`.
3. **npm exec** — `npm exec <binary> -- ...`. Used when `package-lock.json` is present AND `npm` is on `PATH`.
4. **Global** — `<binary>` on `PATH`.
5. **Not found** — `ToCommandPlan()` throws `InvalidOperationException` with an actionable message.

### Pre-flighting (skip when not installed)

`AxeCoreBinaryResolver.IsAvailable(workingDirectory)` returns `true` only when **both** `axe` and `axe-sarif-converter` resolve. Use it to skip the security-pipeline target cleanly when the a11y toolchain isn't installed:

```csharp
if (!AxeCoreBinaryResolver.IsAvailable(WebRoot)) return;
```

For finer-grained checks, call `TryResolveAxe(...)` / `TryResolveSarifConverter(...)` directly.

### Overriding resolution explicitly

```csharp
var binary = new AxeCoreBinaryResolution
{
    Executable = "/opt/ci/axe/4.10.0/axe",
    Source = AxeCoreResolutionSource.Explicit,
};

var plan = AxeCore.Scan(s => s
    .SetBinary(binary)
    .AddUrl("https://staging.example.com")
    .SetOutputFile("axe.json"));
```

## Exit-code semantics

`@axe-core/cli` follows the linter convention; mirrors `Tamp.OpenGrep` / `Tamp.Eslint.V9`:

| Exit | Meaning | Treat as |
|---|---|---|
| `0` | Clean run, no findings | success |
| `1` | Violations reported (when `--exit` is set, which is the default) | success (findings are still a successful scan) |
| `2+` | Tool / config / browser error | failure |

A security-pipeline target should only throw on `exit > 1`.

## Browser runtime

`@axe-core/cli` spawns a headless browser (Chromium by default). In CI containers, you'll typically need:

```bash
# Either pre-install Chromium via Playwright (recommended):
npx playwright install chromium

# Or rely on @axe-core/cli's puppeteer-bundled Chromium and pass --no-sandbox
# (set via SetNoSandbox(true) in the wrapper).
```

If you have a specific Chromium binary path (e.g. system-installed via the runner image), pass it via `SetChromiumBinary(path)`.

## Settings authoring — fluent or object-init

Both styles produce identical `CommandPlan`s; fluent is canonical in docs.

## License

MIT — see [LICENSE](LICENSE).
