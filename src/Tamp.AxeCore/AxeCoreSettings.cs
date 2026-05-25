namespace Tamp.AxeCore;

/// <summary>
/// Shared base for axe-core-family CommandPlan settings. Owns working directory,
/// environment variables, and binary resolution (parameterized by binary name in
/// subclasses).
/// </summary>
public abstract class AxeCoreSettingsBase
{
    /// <summary>Working directory for the spawned process AND root for project-local binary resolution.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Per-invocation environment variables.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; } = new();

    /// <summary>
    /// Explicit binary resolution. When null, <see cref="ToCommandPlan"/> auto-resolves
    /// via <see cref="AxeCoreBinaryResolver.TryResolve"/> against <see cref="WorkingDirectory"/>.
    /// Set this to bypass auto-resolution (e.g. CI agent with a non-standard install path).
    /// </summary>
    public AxeCoreBinaryResolution? Binary { get; set; }

    /// <summary>The npm binary name this verb wraps (subclass-supplied).</summary>
    protected abstract string BinaryName { get; }

    /// <summary>Per-verb argument shaping (called AFTER prefix args, BEFORE any common cleanup).</summary>
    protected abstract IEnumerable<string> BuildVerbArguments();

    /// <summary>Per-verb validation.</summary>
    protected virtual void Validate() { }

    /// <summary>Build the <see cref="CommandPlan"/> for this verb.</summary>
    public CommandPlan ToCommandPlan()
    {
        Validate();

        var resolution = Binary
            ?? AxeCoreBinaryResolver.TryResolve(BinaryName, WorkingDirectory ?? Directory.GetCurrentDirectory())
            ?? throw new InvalidOperationException(
                $"The '{BinaryName}' binary was not found via project-local / pnpm / npm / global resolution. " +
                $"Install via `npm i -D @axe-core/cli axe-sarif-converter` (or pnpm equivalent), or pre-flight with " +
                $"AxeCoreBinaryResolver.IsAvailable(workingDirectory) and skip the scan target when false.");

        var args = new List<string>(resolution.PrefixArguments);
        args.AddRange(BuildVerbArguments());

        return new CommandPlan
        {
            Executable = resolution.Executable,
            Arguments = args,
            Environment = new Dictionary<string, string>(EnvironmentVariables),
            WorkingDirectory = WorkingDirectory,
        };
    }
}

/// <summary>Fluent setters shared by every axe-core-family verb.</summary>
public static class AxeCoreSettingsBaseExtensions
{
    public static T SetWorkingDirectory<T>(this T s, string? cwd) where T : AxeCoreSettingsBase { s.WorkingDirectory = cwd; return s; }
    public static T SetEnvironmentVariable<T>(this T s, string name, string value) where T : AxeCoreSettingsBase { s.EnvironmentVariables[name] = value; return s; }
    public static T SetBinary<T>(this T s, AxeCoreBinaryResolution binary) where T : AxeCoreSettingsBase { s.Binary = binary; return s; }
}

/// <summary>
/// Settings for the <c>axe</c> CLI scan verb. Drives <c>@axe-core/cli</c> against one or more
/// URLs, saving raw axe results JSON to disk.
/// </summary>
/// <remarks>
/// v1 exposes the flags adopters reach for in CI: targets (URLs), output path, WCAG / rule
/// scoping, browser selection, exit-on-violation, timeout, load delay, sandbox toggle.
/// Niche flags (<c>--show-errors</c>, <c>--no-reporter</c>, ad-hoc <c>--dir</c>) can land as
/// <c>SetXxx</c> additions when adopters ask.
/// </remarks>
public sealed class AxeCoreScanSettings : AxeCoreSettingsBase
{
    /// <summary>URLs to scan. Required (at least one).</summary>
    public List<string> Urls { get; } = new();

    /// <summary>
    /// Output file for raw axe JSON (<c>--save</c>). Required — scan results streamed to stdout
    /// aren't useful for downstream SARIF conversion.
    /// </summary>
    public string? OutputFile { get; set; }

    /// <summary>Restrict to specific rule IDs (<c>--rules</c>). Joined with commas.</summary>
    public List<string> Rules { get; } = new();

    /// <summary>Restrict to specific WCAG / categorization tags (<c>--tags</c>). Joined with commas. Common: <c>wcag2a</c>, <c>wcag2aa</c>, <c>wcag21aa</c>, <c>wcag22aa</c>, <c>best-practice</c>.</summary>
    public List<string> Tags { get; } = new();

    /// <summary>CSS selectors to include — only these subtrees are scanned (<c>--include</c>).</summary>
    public List<string> Includes { get; } = new();

    /// <summary>CSS selectors to exclude — these subtrees are skipped (<c>--exclude</c>).</summary>
    public List<string> Excludes { get; } = new();

    /// <summary>Browser engine (<c>--browser</c>). Common values: <c>chromium</c> (default), <c>firefox</c>.</summary>
    public string? Browser { get; set; }

    /// <summary>Path to a specific Chromium binary (<c>--chromium-binary</c>). Useful in CI containers with non-default Chromium installs.</summary>
    public string? ChromiumBinary { get; set; }

    /// <summary>Exit with non-zero when violations are found (<c>--exit</c>). Default true (CI-friendly).</summary>
    public bool Exit { get; set; } = true;

    /// <summary>Per-page timeout in seconds (<c>--timeout</c>).</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Milliseconds to wait after page load before scanning (<c>--load-delay</c>) — for SPAs whose hydration takes time.</summary>
    public int? LoadDelayMs { get; set; }

    /// <summary>Disable the Chromium sandbox (<c>--no-sandbox</c>). Required when running inside Docker / non-privileged containers.</summary>
    public bool NoSandbox { get; set; }

    /// <summary>Suppress the textual reporter (<c>--no-reporter</c>) — keeps stdout clean when only the JSON output matters.</summary>
    public bool NoReporter { get; set; }

    protected override string BinaryName => "axe";

    protected override void Validate()
    {
        if (Urls.Count == 0)
            throw new InvalidOperationException("At least one URL is required (set via AddUrl).");
        if (string.IsNullOrEmpty(OutputFile))
            throw new InvalidOperationException("OutputFile is required (set via SetOutputFile) — raw axe JSON must be saved to disk for downstream SARIF conversion.");
    }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        foreach (var url in Urls) yield return url;
        yield return "--save"; yield return OutputFile!;
        if (Rules.Count > 0) { yield return "--rules"; yield return string.Join(",", Rules); }
        if (Tags.Count > 0) { yield return "--tags"; yield return string.Join(",", Tags); }
        foreach (var inc in Includes) { yield return "--include"; yield return inc; }
        foreach (var exc in Excludes) { yield return "--exclude"; yield return exc; }
        if (!string.IsNullOrEmpty(Browser)) { yield return "--browser"; yield return Browser!; }
        if (!string.IsNullOrEmpty(ChromiumBinary)) { yield return "--chromium-binary"; yield return ChromiumBinary!; }
        if (Exit) yield return "--exit";
        if (TimeoutSeconds is int t) { yield return "--timeout"; yield return t.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        if (LoadDelayMs is int d) { yield return "--load-delay"; yield return d.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        if (NoSandbox) yield return "--no-sandbox";
        if (NoReporter) yield return "--no-reporter";
    }
}

/// <summary>Fluent setters for <see cref="AxeCoreScanSettings"/>.</summary>
public static class AxeCoreScanSettingsExtensions
{
    public static AxeCoreScanSettings AddUrl(this AxeCoreScanSettings s, string url) { s.Urls.Add(url); return s; }
    public static AxeCoreScanSettings SetOutputFile(this AxeCoreScanSettings s, string path) { s.OutputFile = path; return s; }
    public static AxeCoreScanSettings AddRule(this AxeCoreScanSettings s, string ruleId) { s.Rules.Add(ruleId); return s; }
    public static AxeCoreScanSettings AddTag(this AxeCoreScanSettings s, string tag) { s.Tags.Add(tag); return s; }
    public static AxeCoreScanSettings AddInclude(this AxeCoreScanSettings s, string selector) { s.Includes.Add(selector); return s; }
    public static AxeCoreScanSettings AddExclude(this AxeCoreScanSettings s, string selector) { s.Excludes.Add(selector); return s; }
    public static AxeCoreScanSettings SetBrowser(this AxeCoreScanSettings s, string? browser) { s.Browser = browser; return s; }
    public static AxeCoreScanSettings SetChromiumBinary(this AxeCoreScanSettings s, string? path) { s.ChromiumBinary = path; return s; }
    public static AxeCoreScanSettings SetExit(this AxeCoreScanSettings s, bool v = true) { s.Exit = v; return s; }
    public static AxeCoreScanSettings SetTimeoutSeconds(this AxeCoreScanSettings s, int? secs) { s.TimeoutSeconds = secs; return s; }
    public static AxeCoreScanSettings SetLoadDelayMs(this AxeCoreScanSettings s, int? ms) { s.LoadDelayMs = ms; return s; }
    public static AxeCoreScanSettings SetNoSandbox(this AxeCoreScanSettings s, bool v = true) { s.NoSandbox = v; return s; }
    public static AxeCoreScanSettings SetNoReporter(this AxeCoreScanSettings s, bool v = true) { s.NoReporter = v; return s; }
}

/// <summary>
/// Settings for the <c>axe-sarif-converter</c> CLI verb. Converts raw axe JSON to SARIF 2.1.0
/// for the Tamp security pipeline (<c>/ingest/findings</c> in the tamp-ingest-v1 spec).
/// </summary>
public sealed class AxeCoreSarifConvertSettings : AxeCoreSettingsBase
{
    /// <summary>Input axe JSON file (positional). Required.</summary>
    public string? InputFile { get; set; }

    /// <summary>Output SARIF file (positional). Required.</summary>
    public string? OutputFile { get; set; }

    protected override string BinaryName => "axe-sarif-converter";

    protected override void Validate()
    {
        if (string.IsNullOrEmpty(InputFile))
            throw new InvalidOperationException("InputFile is required (set via SetInputFile) — the axe JSON to convert.");
        if (string.IsNullOrEmpty(OutputFile))
            throw new InvalidOperationException("OutputFile is required (set via SetOutputFile) — the SARIF destination path.");
    }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return InputFile!;
        yield return OutputFile!;
    }
}

/// <summary>Fluent setters for <see cref="AxeCoreSarifConvertSettings"/>.</summary>
public static class AxeCoreSarifConvertSettingsExtensions
{
    public static AxeCoreSarifConvertSettings SetInputFile(this AxeCoreSarifConvertSettings s, string path) { s.InputFile = path; return s; }
    public static AxeCoreSarifConvertSettings SetOutputFile(this AxeCoreSarifConvertSettings s, string path) { s.OutputFile = path; return s; }
}
