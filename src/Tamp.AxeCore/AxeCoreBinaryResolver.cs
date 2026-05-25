namespace Tamp.AxeCore;

/// <summary>
/// How an axe-core-family binary (<c>axe</c> or <c>axe-sarif-converter</c>) was located.
/// Used to tag the resolved invocation so adopters / dashboards can distinguish
/// project-local installs from PATH fallbacks.
/// </summary>
public enum AxeCoreResolutionSource
{
    /// <summary>
    /// Project-local install: <c>{workingDirectory}/node_modules/.bin/&lt;binary&gt;</c>
    /// (or <c>.cmd</c> on Windows). Preferred because the project's exact pinned versions run.
    /// </summary>
    ProjectLocal,

    /// <summary>
    /// pnpm exec: <c>pnpm exec &lt;binary&gt; -- ...</c>. Used when a pnpm workspace marker
    /// (<c>pnpm-lock.yaml</c> or <c>pnpm-workspace.yaml</c>) is present at the working
    /// directory but <c>node_modules/.bin/&lt;binary&gt;</c> hasn't been materialized yet.
    /// </summary>
    Pnpm,

    /// <summary>
    /// npm exec: <c>npm exec &lt;binary&gt; -- ...</c>. Used when <c>package-lock.json</c>
    /// is present at the working directory.
    /// </summary>
    Npm,

    /// <summary>Global binary on <c>PATH</c>.</summary>
    Global,

    /// <summary>Adopter passed an explicit <see cref="AxeCoreBinaryResolution"/> via <c>Binary</c> / <c>SetBinary</c>.</summary>
    Explicit,
}

/// <summary>
/// A resolved axe-core-family invocation: the executable to spawn plus any prefix arguments
/// (e.g. <c>exec axe --</c> for the pnpm/npm exec fallbacks). The wrapper threads these
/// into <see cref="CommandPlan.Arguments"/> before the per-scan flags.
/// </summary>
/// <remarks>
/// Constructed by <see cref="AxeCoreBinaryResolver.TryResolveAxe"/> /
/// <see cref="AxeCoreBinaryResolver.TryResolveSarifConverter"/>, or hand-built by adopters
/// who want full control (e.g. a CI agent with a non-standard binary path).
/// </remarks>
public sealed record AxeCoreBinaryResolution
{
    /// <summary>Absolute path to the executable to spawn.</summary>
    public required string Executable { get; init; }

    /// <summary>
    /// Arguments to emit BEFORE the per-verb flags. Empty for direct invocations
    /// (project-local, global). Non-empty for indirect invocations (pnpm/npm exec
    /// need <c>["exec", "&lt;binary&gt;", "--"]</c>).
    /// </summary>
    public IReadOnlyList<string> PrefixArguments { get; init; } = Array.Empty<string>();

    /// <summary>How this resolution was produced.</summary>
    public required AxeCoreResolutionSource Source { get; init; }
}

/// <summary>
/// Locates axe-core-family CLI binaries for a given working directory.
/// Resolution priority for any binary name:
/// <list type="number">
///   <item>Project-local: <c>{workingDirectory}/node_modules/.bin/&lt;binary&gt;</c>.</item>
///   <item>pnpm exec: if <c>pnpm-lock.yaml</c> or <c>pnpm-workspace.yaml</c> exists at the working directory AND <c>pnpm</c> is on <c>PATH</c>.</item>
///   <item>npm exec: if <c>package-lock.json</c> exists at the working directory AND <c>npm</c> is on <c>PATH</c>.</item>
///   <item>Global binary on <c>PATH</c>.</item>
///   <item>null — adopter should skip the target (see <see cref="IsAvailable"/>).</item>
/// </list>
/// </summary>
/// <remarks>
/// Mirrors the resolution shape of <c>Tamp.Eslint.V9.EslintBinaryResolver</c> so that
/// adopters who handle one Node-based wrapper handle them all the same way.
/// </remarks>
public static class AxeCoreBinaryResolver
{
    /// <summary>Resolve the <c>axe</c> CLI (from the <c>@axe-core/cli</c> npm package).</summary>
    public static AxeCoreBinaryResolution? TryResolveAxe(string workingDirectory)
        => TryResolve("axe", workingDirectory);

    /// <summary>Resolve the <c>axe-sarif-converter</c> CLI (from the <c>axe-sarif-converter</c> npm package).</summary>
    public static AxeCoreBinaryResolution? TryResolveSarifConverter(string workingDirectory)
        => TryResolve("axe-sarif-converter", workingDirectory);

    /// <summary>
    /// True when both the <c>axe</c> CLI AND the <c>axe-sarif-converter</c> CLI resolve for
    /// <paramref name="workingDirectory"/>. Use this to pre-flight a security-pipeline target
    /// and skip cleanly when the a11y toolchain isn't installed. If you only need the raw
    /// scan (no SARIF conversion), check <see cref="TryResolveAxe"/> directly.
    /// </summary>
    /// <example>
    /// <code>
    /// protected virtual Target SecurityScanAxeCore => _ => _
    ///     .Executes(() =>
    ///     {
    ///         if (!AxeCoreBinaryResolver.IsAvailable(WebRoot))
    ///         {
    ///             Log.Info("[security] AxeCore skipped — toolchain not installed at {Dir}", WebRoot);
    ///             return;
    ///         }
    ///         var jsonPlan = AxeCore.Scan(s => s.AddUrl(StagingUrl).SetOutputFile(jsonOut).SetWorkingDirectory(WebRoot));
    ///         var sarifPlan = AxeCore.ConvertToSarif(s => s.SetInputFile(jsonOut).SetOutputFile(sarifOut).SetWorkingDirectory(WebRoot));
    ///         ProcessRunner.Execute(jsonPlan);
    ///         ProcessRunner.Execute(sarifPlan);
    ///     });
    /// </code>
    /// </example>
    public static bool IsAvailable(string workingDirectory) =>
        TryResolveAxe(workingDirectory) is not null && TryResolveSarifConverter(workingDirectory) is not null;

    /// <summary>
    /// Generic resolver for an arbitrary binary in the axe-core family (or any sibling Node CLI
    /// in the same project workspace). Returns null when nothing resolves.
    /// </summary>
    public static AxeCoreBinaryResolution? TryResolve(string binary, string workingDirectory)
    {
        if (string.IsNullOrEmpty(binary))
            throw new ArgumentException("binary is required.", nameof(binary));
        if (string.IsNullOrEmpty(workingDirectory))
            throw new ArgumentException("workingDirectory is required.", nameof(workingDirectory));

        var binaryFileName = OperatingSystem.IsWindows() ? $"{binary}.cmd" : binary;

        // 1. Project-local node_modules/.bin/<binary>
        var localBin = Path.Combine(workingDirectory, "node_modules", ".bin", binaryFileName);
        if (File.Exists(localBin))
        {
            return new AxeCoreBinaryResolution
            {
                Executable = localBin,
                Source = AxeCoreResolutionSource.ProjectLocal,
            };
        }

        // 2. pnpm exec — pnpm workspace marker present + pnpm on PATH
        if (HasPnpmWorkspaceMarker(workingDirectory))
        {
            var pnpm = ResolveOnPath("pnpm") ?? ResolveOnPath("pnpm.cmd");
            if (pnpm is not null)
            {
                return new AxeCoreBinaryResolution
                {
                    Executable = pnpm,
                    PrefixArguments = new[] { "exec", binary, "--" },
                    Source = AxeCoreResolutionSource.Pnpm,
                };
            }
        }

        // 3. npm exec — package-lock.json present + npm on PATH
        if (File.Exists(Path.Combine(workingDirectory, "package-lock.json")))
        {
            var npm = ResolveOnPath("npm") ?? ResolveOnPath("npm.cmd");
            if (npm is not null)
            {
                return new AxeCoreBinaryResolution
                {
                    Executable = npm,
                    PrefixArguments = new[] { "exec", binary, "--" },
                    Source = AxeCoreResolutionSource.Npm,
                };
            }
        }

        // 4. Global on PATH
        var global = ResolveOnPath(binaryFileName);
        if (global is not null)
        {
            return new AxeCoreBinaryResolution
            {
                Executable = global,
                Source = AxeCoreResolutionSource.Global,
            };
        }

        return null;
    }

    private static bool HasPnpmWorkspaceMarker(string workingDirectory) =>
        File.Exists(Path.Combine(workingDirectory, "pnpm-lock.yaml")) ||
        File.Exists(Path.Combine(workingDirectory, "pnpm-workspace.yaml"));

    private static string? ResolveOnPath(string binary)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        foreach (var dir in path.Split(separator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            string candidate;
            try { candidate = Path.Combine(dir, binary); }
            catch (ArgumentException) { continue; }
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
