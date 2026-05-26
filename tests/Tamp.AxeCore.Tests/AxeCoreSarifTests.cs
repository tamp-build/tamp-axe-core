using System.Text.Json.Nodes;
using Tamp;
using Tamp.AxeCore;
using Xunit;

namespace Tamp.AxeCore.Tests;

/// <summary>
/// Tests for <see cref="AxeCoreSarif.AnnotateResults"/>. Each test writes a synthetic
/// SARIF file to a temp dir, runs the annotator, and asserts on the resulting JSON
/// shape via JsonNode reads (no Tamp.Sarif typed-model dep — keeps the wrapper's
/// dep surface tight).
/// </summary>
public sealed class AxeCoreSarifTests : IDisposable
{
    private readonly string _tempRoot;

    public AxeCoreSarifTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "tamp-axe-sarif-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    private AbsolutePath WriteSarif(string content)
    {
        var path = Path.Combine(_tempRoot, "axe-" + Guid.NewGuid().ToString("N")[..6] + ".sarif");
        File.WriteAllText(path, content);
        return AbsolutePath.Create(path);
    }

    private static JsonNode ReadJson(AbsolutePath path) =>
        JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidOperationException("parsed null");

    private static IReadOnlyList<string> ResultTags(JsonNode root, int runIdx, int resultIdx)
    {
        var tags = root["runs"]?[runIdx]?["results"]?[resultIdx]?["properties"]?["tags"]?.AsArray();
        if (tags is null) return Array.Empty<string>();
        return tags.Select(n => n!.GetValue<string>()).ToList();
    }

    // ---- Happy path: tags injected on every result ----

    [Fact]
    public void Adds_Tags_To_Every_Result_When_No_Properties_Present()
    {
        var path = WriteSarif("""
        {
          "version": "2.1.0",
          "runs": [
            { "tool": { "driver": { "name": "axe-core" } },
              "results": [
                { "ruleId": "color-contrast", "level": "warning", "message": { "text": "low contrast" } },
                { "ruleId": "region",         "level": "error",   "message": { "text": "no region" } }
              ]
            }
          ]
        }
        """);

        AxeCoreSarif.AnnotateResults(path, "accessibility");

        var root = ReadJson(path);
        Assert.Equal(new[] { "accessibility" }, ResultTags(root, 0, 0));
        Assert.Equal(new[] { "accessibility" }, ResultTags(root, 0, 1));
    }

    [Fact]
    public void Adds_Multiple_Tags_In_Order()
    {
        var path = WriteSarif("""
        { "version": "2.1.0", "runs": [{ "tool": { "driver": { "name": "axe-core" } }, "results": [{ "ruleId": "r1" }] }] }
        """);

        AxeCoreSarif.AnnotateResults(path, "accessibility", "wcag2aa", "axe-core");

        var root = ReadJson(path);
        Assert.Equal(new[] { "accessibility", "wcag2aa", "axe-core" }, ResultTags(root, 0, 0));
    }

    [Fact]
    public void Preserves_Existing_Tags_And_Appends_New()
    {
        var path = WriteSarif("""
        { "version": "2.1.0", "runs": [{ "tool": { "driver": { "name": "axe-core" } }, "results": [
            { "ruleId": "r1", "properties": { "tags": ["wcag2aa", "best-practice"] } }
        ] }] }
        """);

        AxeCoreSarif.AnnotateResults(path, "accessibility");

        var root = ReadJson(path);
        Assert.Equal(new[] { "wcag2aa", "best-practice", "accessibility" }, ResultTags(root, 0, 0));
    }

    [Fact]
    public void Idempotent_When_Tag_Already_Present()
    {
        var path = WriteSarif("""
        { "version": "2.1.0", "runs": [{ "tool": { "driver": { "name": "axe-core" } }, "results": [
            { "ruleId": "r1", "properties": { "tags": ["accessibility"] } }
        ] }] }
        """);

        AxeCoreSarif.AnnotateResults(path, "accessibility");
        AxeCoreSarif.AnnotateResults(path, "accessibility");

        var root = ReadJson(path);
        Assert.Equal(new[] { "accessibility" }, ResultTags(root, 0, 0));
    }

    [Fact]
    public void Preserves_Other_Properties_Untouched()
    {
        var path = WriteSarif("""
        { "version": "2.1.0", "runs": [{ "tool": { "driver": { "name": "axe-core" } }, "results": [
            { "ruleId": "r1", "properties": { "severity": "moderate", "tags": ["wcag2aa"] } }
        ] }] }
        """);

        AxeCoreSarif.AnnotateResults(path, "accessibility");

        var root = ReadJson(path);
        Assert.Equal("moderate", root["runs"]![0]!["results"]![0]!["properties"]!["severity"]!.GetValue<string>());
        Assert.Contains("accessibility", ResultTags(root, 0, 0));
        Assert.Contains("wcag2aa", ResultTags(root, 0, 0));
    }

    [Fact]
    public void Handles_Result_With_Existing_Empty_Tags_Array()
    {
        var path = WriteSarif("""
        { "version": "2.1.0", "runs": [{ "tool": { "driver": { "name": "axe-core" } }, "results": [
            { "ruleId": "r1", "properties": { "tags": [] } }
        ] }] }
        """);

        AxeCoreSarif.AnnotateResults(path, "accessibility");

        Assert.Equal(new[] { "accessibility" }, ResultTags(ReadJson(path), 0, 0));
    }

    [Fact]
    public void Handles_Result_With_Properties_But_No_Tags_Key()
    {
        var path = WriteSarif("""
        { "version": "2.1.0", "runs": [{ "tool": { "driver": { "name": "axe-core" } }, "results": [
            { "ruleId": "r1", "properties": { "severity": "moderate" } }
        ] }] }
        """);

        AxeCoreSarif.AnnotateResults(path, "accessibility");

        Assert.Equal(new[] { "accessibility" }, ResultTags(ReadJson(path), 0, 0));
    }

    [Fact]
    public void Defensive_Replaces_Tags_When_Misshaped_As_Non_Array()
    {
        // Edge: some producers mis-emit "tags" as a single string rather than an array.
        // The annotator replaces with a fresh array rather than throwing (caller can't recover).
        var path = WriteSarif("""
        { "version": "2.1.0", "runs": [{ "tool": { "driver": { "name": "axe-core" } }, "results": [
            { "ruleId": "r1", "properties": { "tags": "string-not-array" } }
        ] }] }
        """);

        AxeCoreSarif.AnnotateResults(path, "accessibility");

        var root = ReadJson(path);
        Assert.Equal(new[] { "accessibility" }, ResultTags(root, 0, 0));
    }

    // ---- Multi-run / multi-result ----

    [Fact]
    public void Walks_All_Runs_And_All_Results()
    {
        var path = WriteSarif("""
        { "version": "2.1.0", "runs": [
            { "tool": { "driver": { "name": "axe-core" } }, "results": [
                { "ruleId": "r1" }, { "ruleId": "r2" }
            ]},
            { "tool": { "driver": { "name": "axe-core" } }, "results": [
                { "ruleId": "r3" }
            ]}
        ] }
        """);

        AxeCoreSarif.AnnotateResults(path, "accessibility");

        var root = ReadJson(path);
        Assert.Equal(new[] { "accessibility" }, ResultTags(root, 0, 0));
        Assert.Equal(new[] { "accessibility" }, ResultTags(root, 0, 1));
        Assert.Equal(new[] { "accessibility" }, ResultTags(root, 1, 0));
    }

    // ---- Edge cases that should no-op cleanly ----

    [Fact]
    public void Empty_Tags_Is_No_Op_Does_Not_Touch_File()
    {
        var content = """
        { "version": "2.1.0", "runs": [{ "tool": { "driver": { "name": "axe-core" } }, "results": [{ "ruleId": "r1" }] }] }
        """;
        var path = WriteSarif(content);
        var modifiedBefore = File.GetLastWriteTimeUtc(path);

        // Sleep briefly to make mtime-skew observable if the file IS touched.
        Thread.Sleep(50);
        AxeCoreSarif.AnnotateResults(path);

        var modifiedAfter = File.GetLastWriteTimeUtc(path);
        Assert.Equal(modifiedBefore, modifiedAfter);
    }

    [Fact]
    public void Empty_String_Tag_Is_Skipped()
    {
        var path = WriteSarif("""
        { "version": "2.1.0", "runs": [{ "tool": { "driver": { "name": "axe-core" } }, "results": [{ "ruleId": "r1" }] }] }
        """);

        AxeCoreSarif.AnnotateResults(path, "", "accessibility", "");

        // Only "accessibility" makes it through; empty strings are dropped.
        Assert.Equal(new[] { "accessibility" }, ResultTags(ReadJson(path), 0, 0));
    }

    [Fact]
    public void Run_With_Null_Results_Is_Skipped()
    {
        var path = WriteSarif("""
        { "version": "2.1.0", "runs": [
            { "tool": { "driver": { "name": "axe-core" } } },
            { "tool": { "driver": { "name": "axe-core" } }, "results": [{ "ruleId": "r1" }] }
        ] }
        """);

        AxeCoreSarif.AnnotateResults(path, "accessibility");

        // No exception; first run unchanged (no results to annotate), second run has the tag.
        Assert.Equal(new[] { "accessibility" }, ResultTags(ReadJson(path), 1, 0));
    }

    [Fact]
    public void Sarif_With_No_Runs_Is_No_Op()
    {
        var path = WriteSarif("""{ "version": "2.1.0" }""");
        // Doesn't throw.
        AxeCoreSarif.AnnotateResults(path, "accessibility");
        // File still parseable.
        Assert.NotNull(ReadJson(path));
    }

    // ---- Argument / file errors ----

    [Fact]
    public void Null_SarifPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AxeCoreSarif.AnnotateResults(null!, "accessibility"));
    }

    [Fact]
    public void Null_Tags_Throws()
    {
        var path = AbsolutePath.Create(Path.Combine(_tempRoot, "x.sarif"));
        Assert.Throws<ArgumentNullException>(() => AxeCoreSarif.AnnotateResults(path, (string[])null!));
    }

    [Fact]
    public void Missing_File_Throws_FileNotFound()
    {
        var path = AbsolutePath.Create(Path.Combine(_tempRoot, "does-not-exist.sarif"));
        var ex = Assert.Throws<FileNotFoundException>(() => AxeCoreSarif.AnnotateResults(path, "accessibility"));
        Assert.Contains("does-not-exist", ex.Message);
    }

    [Fact]
    public void Invalid_Json_Throws_InvalidData()
    {
        var path = WriteSarif("{not valid json");
        var ex = Assert.Throws<InvalidDataException>(() => AxeCoreSarif.AnnotateResults(path, "accessibility"));
        Assert.Contains("not valid JSON", ex.Message);
    }
}
