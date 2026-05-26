using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tamp.AxeCore;

/// <summary>
/// Post-processing helpers for SARIF files produced by the axe-core toolchain
/// (<see cref="AxeCore.Scan"/> → <see cref="AxeCore.ConvertToSarif"/>).
/// </summary>
/// <remarks>
/// <para>
/// <c>@axe-core/cli</c> and <c>axe-sarif-converter</c> together produce SARIF
/// 2.1.0 but don't stamp domain-classification properties (sub-category, scanner
/// kind, etc.) on individual results. Downstream sinks therefore can't route by
/// category without filename / scanner-name heuristics. <see cref="AnnotateResults"/>
/// fills that gap by injecting <c>properties.tags</c> entries on every result
/// after conversion — same shape Trivy emits natively.
/// </para>
/// <para>
/// <strong>Why this isn't a CommandPlan verb.</strong> The Tamp creed is one
/// wrapper per CLI tool; the verbs in <see cref="AxeCore"/> emit <c>CommandPlan</c>s
/// that shell out to <c>axe</c> / <c>axe-sarif-converter</c>. This helper performs
/// in-process file manipulation — no external tool involved — so it lives as a
/// separate static helper rather than a verb on the facade.
/// </para>
/// <para>
/// <strong>Typical use.</strong>
/// </para>
/// <code>
/// ProcessRunner.Execute(AxeCore.Scan(s => s.AddUrl(url).SetOutputFile(jsonPath)));
/// ProcessRunner.Execute(AxeCore.ConvertToSarif(s => s.SetInputFile(jsonPath).SetOutputFile(sarifPath)));
/// AxeCoreSarif.AnnotateResults(sarifPath, "accessibility");
/// // sarifPath now has properties.tags: ["accessibility"] on every result.
/// </code>
/// </remarks>
public static class AxeCoreSarif
{
    /// <summary>
    /// Add the given <paramref name="tags"/> to every <c>runs[*].results[*].properties.tags</c>
    /// in the SARIF at <paramref name="sarifPath"/>, preserving existing tags and
    /// deduplicating (case-sensitive per SARIF spec). Writes the result back to
    /// the same path. Empty tag list is a no-op (file isn't touched).
    /// </summary>
    /// <param name="sarifPath">Path to a SARIF 2.1.0 JSON file produced by <see cref="AxeCore.ConvertToSarif"/>.</param>
    /// <param name="tags">Tag values to inject. Common values: <c>"accessibility"</c>, <c>"wcag2aa"</c>, etc.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sarifPath"/> or <paramref name="tags"/> is null.</exception>
    /// <exception cref="FileNotFoundException"><paramref name="sarifPath"/> does not exist.</exception>
    /// <exception cref="InvalidDataException">The file isn't valid JSON or isn't shaped like SARIF.</exception>
    public static void AnnotateResults(AbsolutePath sarifPath, params string[] tags)
    {
        if (sarifPath is null) throw new ArgumentNullException(nameof(sarifPath));
        if (tags is null) throw new ArgumentNullException(nameof(tags));
        if (tags.Length == 0) return;

        if (!File.Exists(sarifPath))
            throw new FileNotFoundException($"SARIF file not found at '{sarifPath.Value}'.", sarifPath.Value);

        JsonNode root;
        try
        {
            var json = File.ReadAllText(sarifPath);
            root = JsonNode.Parse(json)
                ?? throw new InvalidDataException($"SARIF at '{sarifPath.Value}' parsed to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"SARIF at '{sarifPath.Value}' is not valid JSON: {ex.Message}", ex);
        }

        var runs = root["runs"]?.AsArray();
        if (runs is null) return; // No runs to annotate (well-formed but trivially empty SARIF).

        foreach (var run in runs)
        {
            var results = run?["results"]?.AsArray();
            if (results is null) continue;

            foreach (var result in results)
            {
                if (result is null) continue;

                // Ensure result.properties exists.
                if (result["properties"] is null)
                {
                    result["properties"] = new JsonObject();
                }
                var props = result["properties"]!.AsObject();

                // Ensure properties.tags exists as an array (don't clobber if it's already there).
                if (props["tags"] is null)
                {
                    props["tags"] = new JsonArray();
                }
                else if (props["tags"] is not JsonArray)
                {
                    // Defensive: some producers misuse the field. Replace with a fresh array; the
                    // alternative is to throw, which is worse since the caller can't recover.
                    props["tags"] = new JsonArray();
                }
                var tagArray = (JsonArray)props["tags"]!;

                // Build the existing tag set for idempotent merge.
                var existing = new HashSet<string>(StringComparer.Ordinal);
                foreach (var node in tagArray)
                {
                    if (node is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrEmpty(s))
                    {
                        existing.Add(s);
                    }
                }

                // Append tags that aren't already present.
                foreach (var tag in tags)
                {
                    if (string.IsNullOrEmpty(tag)) continue;
                    if (existing.Add(tag))
                    {
                        tagArray.Add(JsonValue.Create(tag));
                    }
                }
            }
        }

        var serialized = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(sarifPath, serialized);
    }
}
