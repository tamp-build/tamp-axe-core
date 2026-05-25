using System.Linq;
using Tamp.AxeCore;
using Xunit;

namespace Tamp.AxeCore.Tests;

/// <summary>
/// Filesystem-based tests for <see cref="AxeCoreBinaryResolver"/>. Each test stands
/// up a temp directory simulating a real project layout, then asserts the resolution
/// outcome. PATH-based resolution is exercised by stubbing PATH to a temp dir.
/// </summary>
[Collection("AxeCorePathSensitive")]
public sealed class AxeCoreBinaryResolverTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string? _originalPath;

    public AxeCoreBinaryResolverTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "tamp-axe-resolver-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
        _originalPath = Environment.GetEnvironmentVariable("PATH");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    private string MakeProject(string subdir)
    {
        var dir = Path.Combine(_tempRoot, subdir);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string BinName(string bin) => OperatingSystem.IsWindows() ? $"{bin}.cmd" : bin;

    private string PutExecutable(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, OperatingSystem.IsWindows() ? "@echo off\r\necho fake\r\n" : "#!/bin/sh\necho fake\n");
        if (!OperatingSystem.IsWindows())
        {
            try { System.Diagnostics.Process.Start("chmod", $"+x \"{path}\"")?.WaitForExit(); } catch { }
        }
        return path;
    }

    // ---- Project-local resolution ----

    [Fact]
    public void Resolves_ProjectLocal_When_node_modules_bin_axe_Exists()
    {
        var project = MakeProject("with-local-axe");
        Directory.CreateDirectory(Path.Combine(project, "node_modules", ".bin"));
        var localBin = Path.Combine(project, "node_modules", ".bin", BinName("axe"));
        File.WriteAllText(localBin, "fake");

        var resolved = AxeCoreBinaryResolver.TryResolveAxe(project);

        Assert.NotNull(resolved);
        Assert.Equal(AxeCoreResolutionSource.ProjectLocal, resolved!.Source);
        Assert.Equal(localBin, resolved.Executable);
        Assert.Empty(resolved.PrefixArguments);
    }

    [Fact]
    public void Resolves_ProjectLocal_For_SarifConverter_Independently_Of_Axe()
    {
        var project = MakeProject("axe-yes-converter-yes");
        Directory.CreateDirectory(Path.Combine(project, "node_modules", ".bin"));
        var axeBin = Path.Combine(project, "node_modules", ".bin", BinName("axe"));
        var convBin = Path.Combine(project, "node_modules", ".bin", BinName("axe-sarif-converter"));
        File.WriteAllText(axeBin, "fake");
        File.WriteAllText(convBin, "fake");

        var axeRes = AxeCoreBinaryResolver.TryResolveAxe(project);
        var convRes = AxeCoreBinaryResolver.TryResolveSarifConverter(project);

        Assert.Equal(axeBin, axeRes!.Executable);
        Assert.Equal(convBin, convRes!.Executable);
    }

    [Fact]
    public void Generic_TryResolve_Works_For_Arbitrary_Binary_Name()
    {
        var project = MakeProject("arbitrary-bin");
        Directory.CreateDirectory(Path.Combine(project, "node_modules", ".bin"));
        var someBin = Path.Combine(project, "node_modules", ".bin", BinName("some-other-tool"));
        File.WriteAllText(someBin, "fake");

        var resolved = AxeCoreBinaryResolver.TryResolve("some-other-tool", project);

        Assert.NotNull(resolved);
        Assert.Equal(AxeCoreResolutionSource.ProjectLocal, resolved!.Source);
    }

    // ---- pnpm exec resolution ----

    [Fact]
    public void Resolves_Pnpm_Exec_When_pnpm_lock_And_pnpm_On_PATH()
    {
        var project = MakeProject("pnpm-project");
        File.WriteAllText(Path.Combine(project, "pnpm-lock.yaml"), "lockfileVersion: '6.0'");

        var pathStub = MakeProject("path-stub-pnpm");
        var pnpmBin = PutExecutable(pathStub, BinName("pnpm"));
        Environment.SetEnvironmentVariable("PATH", pathStub);

        var resolved = AxeCoreBinaryResolver.TryResolveAxe(project);

        Assert.NotNull(resolved);
        Assert.Equal(AxeCoreResolutionSource.Pnpm, resolved!.Source);
        Assert.Equal(pnpmBin, resolved.Executable);
        Assert.Equal(new[] { "exec", "axe", "--" }, resolved.PrefixArguments.ToArray());
    }

    [Fact]
    public void Pnpm_Marker_Without_Pnpm_On_PATH_Falls_Through_To_Null()
    {
        var project = MakeProject("pnpm-no-binary");
        File.WriteAllText(Path.Combine(project, "pnpm-lock.yaml"), "lockfileVersion: '6.0'");
        Environment.SetEnvironmentVariable("PATH", MakeProject("path-stub-empty"));

        var resolved = AxeCoreBinaryResolver.TryResolveAxe(project);
        Assert.Null(resolved);
    }

    [Fact]
    public void Resolves_Pnpm_Exec_From_pnpm_workspace_Yaml_Marker_Too()
    {
        var project = MakeProject("pnpm-workspace-marker");
        File.WriteAllText(Path.Combine(project, "pnpm-workspace.yaml"), "packages:\n  - 'packages/*'");

        var pathStub = MakeProject("path-stub-pnpm-ws");
        PutExecutable(pathStub, BinName("pnpm"));
        Environment.SetEnvironmentVariable("PATH", pathStub);

        var resolved = AxeCoreBinaryResolver.TryResolveAxe(project);
        Assert.Equal(AxeCoreResolutionSource.Pnpm, resolved!.Source);
    }

    // ---- npm exec resolution ----

    [Fact]
    public void Resolves_Npm_Exec_When_package_lock_And_npm_On_PATH()
    {
        var project = MakeProject("npm-project");
        File.WriteAllText(Path.Combine(project, "package-lock.json"), "{}");

        var pathStub = MakeProject("path-stub-npm");
        var npmBin = PutExecutable(pathStub, BinName("npm"));
        Environment.SetEnvironmentVariable("PATH", pathStub);

        var resolved = AxeCoreBinaryResolver.TryResolveSarifConverter(project);

        Assert.NotNull(resolved);
        Assert.Equal(AxeCoreResolutionSource.Npm, resolved!.Source);
        Assert.Equal(npmBin, resolved.Executable);
        Assert.Equal(new[] { "exec", "axe-sarif-converter", "--" }, resolved.PrefixArguments.ToArray());
    }

    [Fact]
    public void Pnpm_Beats_Npm_When_Both_Markers_Present()
    {
        var project = MakeProject("pnpm-and-npm");
        File.WriteAllText(Path.Combine(project, "pnpm-lock.yaml"), "lockfileVersion: '6.0'");
        File.WriteAllText(Path.Combine(project, "package-lock.json"), "{}");

        var pathStub = MakeProject("path-stub-both");
        PutExecutable(pathStub, BinName("pnpm"));
        PutExecutable(pathStub, BinName("npm"));
        Environment.SetEnvironmentVariable("PATH", pathStub);

        var resolved = AxeCoreBinaryResolver.TryResolveAxe(project);
        Assert.Equal(AxeCoreResolutionSource.Pnpm, resolved!.Source);
    }

    // ---- Global PATH resolution ----

    [Fact]
    public void Resolves_Global_axe_From_PATH_When_No_Project_Markers()
    {
        var project = MakeProject("no-markers");
        var pathStub = MakeProject("path-stub-global");
        var globalBin = PutExecutable(pathStub, BinName("axe"));
        Environment.SetEnvironmentVariable("PATH", pathStub);

        var resolved = AxeCoreBinaryResolver.TryResolveAxe(project);

        Assert.NotNull(resolved);
        Assert.Equal(AxeCoreResolutionSource.Global, resolved!.Source);
        Assert.Equal(globalBin, resolved.Executable);
        Assert.Empty(resolved.PrefixArguments);
    }

    // ---- IsAvailable composite ----

    [Fact]
    public void IsAvailable_Requires_BOTH_axe_AND_converter()
    {
        // Only axe installed
        var project = MakeProject("only-axe");
        Directory.CreateDirectory(Path.Combine(project, "node_modules", ".bin"));
        File.WriteAllText(Path.Combine(project, "node_modules", ".bin", BinName("axe")), "fake");
        Environment.SetEnvironmentVariable("PATH", MakeProject("path-stub-empty"));

        Assert.NotNull(AxeCoreBinaryResolver.TryResolveAxe(project));
        Assert.Null(AxeCoreBinaryResolver.TryResolveSarifConverter(project));
        Assert.False(AxeCoreBinaryResolver.IsAvailable(project));
    }

    [Fact]
    public void IsAvailable_True_When_Both_Resolve()
    {
        var project = MakeProject("both-resolve");
        Directory.CreateDirectory(Path.Combine(project, "node_modules", ".bin"));
        File.WriteAllText(Path.Combine(project, "node_modules", ".bin", BinName("axe")), "fake");
        File.WriteAllText(Path.Combine(project, "node_modules", ".bin", BinName("axe-sarif-converter")), "fake");

        Assert.True(AxeCoreBinaryResolver.IsAvailable(project));
    }

    // ---- No-resolution path ----

    [Fact]
    public void TryResolve_Returns_Null_When_Nothing_Found()
    {
        var project = MakeProject("empty");
        Environment.SetEnvironmentVariable("PATH", MakeProject("path-stub-nothing"));

        Assert.Null(AxeCoreBinaryResolver.TryResolveAxe(project));
        Assert.Null(AxeCoreBinaryResolver.TryResolveSarifConverter(project));
    }

    // ---- Argument validation ----

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void TryResolveAxe_With_Empty_WorkingDirectory_Throws(string? cwd)
    {
        Assert.Throws<ArgumentException>(() => AxeCoreBinaryResolver.TryResolveAxe(cwd!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Generic_TryResolve_With_Empty_Binary_Throws(string? binary)
    {
        Assert.Throws<ArgumentException>(() => AxeCoreBinaryResolver.TryResolve(binary!, "/tmp"));
    }
}
