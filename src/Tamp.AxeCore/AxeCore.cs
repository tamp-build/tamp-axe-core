namespace Tamp.AxeCore;

/// <summary>
/// Tamp wrappers for standalone web accessibility scanning. Two verbs cover the canonical
/// flow: <see cref="Scan"/> drives <c>@axe-core/cli</c> against a deployed URL to produce
/// raw axe JSON; <see cref="ConvertToSarif"/> drives <c>axe-sarif-converter</c> to convert
/// that JSON to SARIF 2.1.0 for the Tamp security pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why two verbs.</strong> Unlike ESLint or Trivy, <c>@axe-core/cli</c> does not
/// emit SARIF natively — it produces axe's own JSON shape. The Tamp security chain
/// consumes SARIF, so a separate conversion step is needed. Adopters compose them in a
/// target:
/// </para>
/// <code>
/// Target SecurityScanAxeCore => _ => _.Executes(() =>
/// {
///     if (!AxeCoreBinaryResolver.IsAvailable(WebRoot)) { Log.Info("...skipped..."); return; }
///     var json  = SecurityArtifactsDir / "axe.json";
///     var sarif = SecurityArtifactsDir / "axe.sarif";
///
///     var scanPlan = AxeCore.Scan(s => s
///         .AddUrl(StagingUrl)
///         .SetOutputFile(json)
///         .AddTag("wcag2a").AddTag("wcag2aa").AddTag("wcag21aa")
///         .SetWorkingDirectory(WebRoot));
///     ProcessRunner.Execute(scanPlan);
///
///     var sarifPlan = AxeCore.ConvertToSarif(s => s
///         .SetInputFile(json)
///         .SetOutputFile(sarif)
///         .SetWorkingDirectory(WebRoot));
///     ProcessRunner.Execute(sarifPlan);
/// });
/// </code>
/// <para>
/// <strong>Exit-code semantics.</strong> Mirrors <c>Tamp.OpenGrep</c> / <c>Tamp.Eslint.V9</c>:
/// <c>axe</c> exits <c>0</c> on a clean run; <c>1</c> when violations are found AND
/// <see cref="AxeCoreScanSettings.Exit"/> is true; non-zero on tool errors. Adopters writing
/// security-pipeline targets should treat <c>0</c> and <c>1</c> as a successful scan and
/// only fail the target on tool errors (typically <c>exit &gt; 1</c>, but adopter discretion).
/// </para>
/// <para>
/// <strong>In-browser-test a11y instead.</strong> If you already have Playwright tests, the
/// <c>@axe-core/playwright</c> JS package is a better fit — install via <c>Tamp.Npm</c>,
/// import inside your existing test files, and the findings flow through your test results.
/// <c>Tamp.AxeCore</c> is specifically for standalone URL scans against deployed
/// environments where you don't have an in-test path.
/// </para>
/// </remarks>
public static class AxeCore
{
    /// <summary><c>axe &lt;urls...&gt; --save &lt;output&gt; [flags]</c> — scan deployed URLs.</summary>
    public static CommandPlan Scan(Action<AxeCoreScanSettings> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        var settings = new AxeCoreScanSettings();
        configure(settings);
        return settings.ToCommandPlan();
    }

    /// <summary>Object-init overload. Identical CommandPlan to the fluent path.</summary>
    public static CommandPlan Scan(AxeCoreScanSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        return settings.ToCommandPlan();
    }

    /// <summary><c>axe-sarif-converter &lt;input.json&gt; &lt;output.sarif&gt;</c> — convert axe JSON to SARIF 2.1.0.</summary>
    public static CommandPlan ConvertToSarif(Action<AxeCoreSarifConvertSettings> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        var settings = new AxeCoreSarifConvertSettings();
        configure(settings);
        return settings.ToCommandPlan();
    }

    /// <summary>Object-init overload. Identical CommandPlan to the fluent path.</summary>
    public static CommandPlan ConvertToSarif(AxeCoreSarifConvertSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        return settings.ToCommandPlan();
    }
}
