using System.Linq;
using Bogus;
using Tamp;
using Tamp.AxeCore;
using Xunit;

namespace Tamp.AxeCore.Tests;

// Serialize with AxeCoreBinaryResolverTests — both mutate process-wide PATH.
[Collection("AxeCorePathSensitive")]
public sealed class AxeCoreTests
{
    private static AxeCoreBinaryResolution FakeAxeBinary() => new()
    {
        Executable = OperatingSystem.IsWindows() ? "C:\\fake\\axe.cmd" : "/fake/axe",
        Source = AxeCoreResolutionSource.Explicit,
    };

    private static AxeCoreBinaryResolution FakeConverterBinary() => new()
    {
        Executable = OperatingSystem.IsWindows() ? "C:\\fake\\axe-sarif-converter.cmd" : "/fake/axe-sarif-converter",
        Source = AxeCoreResolutionSource.Explicit,
    };

    private static int IndexOf(IReadOnlyList<string> args, string token)
    {
        for (var i = 0; i < args.Count; i++) if (args[i] == token) return i;
        return -1;
    }

    // ============== Scan verb ==============

    [Fact]
    public void Scan_Single_Url_Then_Save_Flag()
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary())
            .AddUrl("https://example.com/admin")
            .SetOutputFile("axe.json"));

        Assert.Equal("https://example.com/admin", plan.Arguments[0]);
        var idx = IndexOf(plan.Arguments, "--save");
        Assert.Equal("axe.json", plan.Arguments[idx + 1]);
    }

    [Fact]
    public void Scan_Multiple_Urls_All_Lead()
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary())
            .AddUrl("https://a.example.com")
            .AddUrl("https://b.example.com")
            .AddUrl("https://c.example.com")
            .SetOutputFile("axe.json"));

        Assert.Equal(new[] { "https://a.example.com", "https://b.example.com", "https://c.example.com" },
            plan.Arguments.Take(3).ToArray());
    }

    [Fact]
    public void Scan_Without_Url_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary())
            .SetOutputFile("out.json")));
        Assert.Contains("URL", ex.Message);
    }

    [Fact]
    public void Scan_Without_OutputFile_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary())
            .AddUrl("https://example.com")));
        Assert.Contains("OutputFile", ex.Message);
    }

    // ---- Rules + Tags (comma-joined) ----

    [Fact]
    public void Rules_Joined_With_Commas()
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary()).AddUrl("https://x").SetOutputFile("out.json")
            .AddRule("color-contrast")
            .AddRule("region")
            .AddRule("landmark-one-main"));

        var idx = IndexOf(plan.Arguments, "--rules");
        Assert.Equal("color-contrast,region,landmark-one-main", plan.Arguments[idx + 1]);
        Assert.Single(plan.Arguments, a => a == "--rules");
    }

    [Fact]
    public void Tags_Joined_With_Commas_WCAG_Categories()
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary()).AddUrl("https://x").SetOutputFile("out.json")
            .AddTag("wcag2a")
            .AddTag("wcag2aa")
            .AddTag("wcag21aa")
            .AddTag("best-practice"));

        var idx = IndexOf(plan.Arguments, "--tags");
        Assert.Equal("wcag2a,wcag2aa,wcag21aa,best-practice", plan.Arguments[idx + 1]);
    }

    [Fact]
    public void No_Rules_No_Tags_Omits_Both_Flags()
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary()).AddUrl("https://x").SetOutputFile("out.json"));
        Assert.DoesNotContain("--rules", plan.Arguments);
        Assert.DoesNotContain("--tags", plan.Arguments);
    }

    // ---- Includes / Excludes ----

    [Fact]
    public void Includes_Each_Get_Their_Own_Flag()
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary()).AddUrl("https://x").SetOutputFile("out.json")
            .AddInclude("main")
            .AddInclude("#content"));

        var incFlags = plan.Arguments.Count(a => a == "--include");
        Assert.Equal(2, incFlags);
        Assert.Contains("main", plan.Arguments);
        Assert.Contains("#content", plan.Arguments);
    }

    [Fact]
    public void Excludes_Each_Get_Their_Own_Flag()
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary()).AddUrl("https://x").SetOutputFile("out.json")
            .AddExclude(".third-party-widget")
            .AddExclude("[data-test='skip-axe']"));

        var excFlags = plan.Arguments.Count(a => a == "--exclude");
        Assert.Equal(2, excFlags);
        Assert.Contains(".third-party-widget", plan.Arguments);
        Assert.Contains("[data-test='skip-axe']", plan.Arguments);
    }

    // ---- Browser + chromium binary ----

    [Theory]
    [InlineData("chromium")]
    [InlineData("firefox")]
    public void Browser_Emits_Flag(string browser)
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary()).AddUrl("https://x").SetOutputFile("out.json")
            .SetBrowser(browser));
        var idx = IndexOf(plan.Arguments, "--browser");
        Assert.Equal(browser, plan.Arguments[idx + 1]);
    }

    [Fact]
    public void ChromiumBinary_Emits_Flag_With_Path()
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary()).AddUrl("https://x").SetOutputFile("out.json")
            .SetChromiumBinary("/opt/google/chrome/chrome"));
        var idx = IndexOf(plan.Arguments, "--chromium-binary");
        Assert.Equal("/opt/google/chrome/chrome", plan.Arguments[idx + 1]);
    }

    // ---- Behavioral flags ----

    [Fact]
    public void Exit_Default_True_Emits_Flag()
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary()).AddUrl("https://x").SetOutputFile("out.json"));
        Assert.Contains("--exit", plan.Arguments);
    }

    [Fact]
    public void Exit_False_Omits_Flag()
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary()).AddUrl("https://x").SetOutputFile("out.json")
            .SetExit(false));
        Assert.DoesNotContain("--exit", plan.Arguments);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(60)]
    [InlineData(300)]
    public void TimeoutSeconds_Emits_Value(int secs)
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary()).AddUrl("https://x").SetOutputFile("out.json")
            .SetTimeoutSeconds(secs));
        var idx = IndexOf(plan.Arguments, "--timeout");
        Assert.Equal(secs.ToString(System.Globalization.CultureInfo.InvariantCulture), plan.Arguments[idx + 1]);
    }

    [Fact]
    public void LoadDelayMs_Emits_Value()
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary()).AddUrl("https://x").SetOutputFile("out.json")
            .SetLoadDelayMs(2500));
        var idx = IndexOf(plan.Arguments, "--load-delay");
        Assert.Equal("2500", plan.Arguments[idx + 1]);
    }

    [Fact]
    public void NoSandbox_Emits_Flag()
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary()).AddUrl("https://x").SetOutputFile("out.json")
            .SetNoSandbox());
        Assert.Contains("--no-sandbox", plan.Arguments);
    }

    [Fact]
    public void NoReporter_Emits_Flag()
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary()).AddUrl("https://x").SetOutputFile("out.json")
            .SetNoReporter());
        Assert.Contains("--no-reporter", plan.Arguments);
    }

    // ---- Binary resolution (Scan) ----

    [Fact]
    public void Scan_Binary_Executable_Flows_To_CommandPlan_Executable()
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary()).AddUrl("https://x").SetOutputFile("out.json"));
        Assert.Equal(FakeAxeBinary().Executable, plan.Executable);
    }

    [Fact]
    public void Scan_Binary_PrefixArguments_Lead_The_Arg_List()
    {
        var binary = new AxeCoreBinaryResolution
        {
            Executable = "/usr/local/bin/pnpm",
            PrefixArguments = new[] { "exec", "axe", "--" },
            Source = AxeCoreResolutionSource.Pnpm,
        };

        var plan = AxeCore.Scan(s => s
            .SetBinary(binary)
            .AddUrl("https://x").SetOutputFile("out.json"));

        Assert.Equal("/usr/local/bin/pnpm", plan.Executable);
        Assert.Equal(new[] { "exec", "axe", "--" }, plan.Arguments.Take(3).ToArray());
        Assert.Equal("https://x", plan.Arguments[3]);
    }

    [Fact]
    public void Scan_No_Binary_And_No_Auto_Resolve_Throws_With_Actionable_Message()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tamp-axe-tests-" + Guid.NewGuid().ToString("N")[..8]);
        var emptyPath = Path.Combine(Path.GetTempPath(), "tamp-axe-emptypath-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(emptyPath);
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", emptyPath);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                AxeCore.Scan(s => s.AddUrl("https://x").SetOutputFile("out.json").SetWorkingDirectory(tempDir)));

            Assert.Contains("axe", ex.Message);
            Assert.Contains("IsAvailable", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            try { Directory.Delete(emptyPath, recursive: true); } catch { }
        }
    }

    // ---- Cross-cutting (Scan) ----

    [Fact]
    public void WorkingDirectory_Propagates()
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary()).AddUrl("https://x").SetOutputFile("out.json")
            .SetWorkingDirectory("/tmp/web"));
        Assert.Equal("/tmp/web", plan.WorkingDirectory);
    }

    [Fact]
    public void Environment_Variables_Propagate()
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary()).AddUrl("https://x").SetOutputFile("out.json")
            .SetEnvironmentVariable("PUPPETEER_SKIP_DOWNLOAD", "true"));
        Assert.Equal("true", plan.Environment["PUPPETEER_SKIP_DOWNLOAD"]);
    }

    // ---- Object-init parity (Scan) ----

    [Fact]
    public void Scan_ObjectInit_Identical_To_Fluent()
    {
        var bin = FakeAxeBinary();
        var fluent = AxeCore.Scan(s => s
            .SetBinary(bin)
            .AddUrl("https://staging.example.com")
            .SetOutputFile("artifacts/axe.json")
            .AddTag("wcag2a").AddTag("wcag2aa")
            .AddExclude(".third-party")
            .SetBrowser("chromium")
            .SetExit()
            .SetTimeoutSeconds(60)
            .SetNoSandbox());

        var settings = new AxeCoreScanSettings
        {
            Binary = bin,
            OutputFile = "artifacts/axe.json",
            Browser = "chromium",
            Exit = true,
            TimeoutSeconds = 60,
            NoSandbox = true,
        };
        settings.Urls.Add("https://staging.example.com");
        settings.Tags.Add("wcag2a");
        settings.Tags.Add("wcag2aa");
        settings.Excludes.Add(".third-party");
        var objInit = AxeCore.Scan(settings);

        Assert.Equal(fluent.Arguments, objInit.Arguments);
        Assert.Equal(fluent.Executable, objInit.Executable);
    }

    // ---- Realistic CI invocation ----

    [Fact]
    public void Realistic_Security_Pipeline_Scan_Shape()
    {
        var bin = new AxeCoreBinaryResolution
        {
            Executable = "/work/web/node_modules/.bin/axe",
            Source = AxeCoreResolutionSource.ProjectLocal,
        };

        var plan = AxeCore.Scan(s => s
            .SetBinary(bin)
            .SetWorkingDirectory("/work/web")
            .AddUrl("https://staging.example.com/")
            .AddUrl("https://staging.example.com/admin")
            .SetOutputFile("/work/artifacts/security/axe.json")
            .AddTag("wcag2a").AddTag("wcag2aa").AddTag("wcag21aa").AddTag("best-practice")
            .AddExclude(".third-party-widget")
            .SetBrowser("chromium")
            .SetExit()
            .SetTimeoutSeconds(60)
            .SetLoadDelayMs(2000)
            .SetNoSandbox()
            .SetNoReporter());

        Assert.Equal("/work/web/node_modules/.bin/axe", plan.Executable);
        Assert.Equal("/work/web", plan.WorkingDirectory);

        // URLs lead
        Assert.Equal("https://staging.example.com/", plan.Arguments[0]);
        Assert.Equal("https://staging.example.com/admin", plan.Arguments[1]);

        // Critical tokens all present
        Assert.Contains("--save", plan.Arguments);
        Assert.Contains("/work/artifacts/security/axe.json", plan.Arguments);
        Assert.Contains("--tags", plan.Arguments);
        Assert.Contains("wcag2a,wcag2aa,wcag21aa,best-practice", plan.Arguments);
        Assert.Contains("--exclude", plan.Arguments);
        Assert.Contains(".third-party-widget", plan.Arguments);
        Assert.Contains("--browser", plan.Arguments);
        Assert.Contains("chromium", plan.Arguments);
        Assert.Contains("--exit", plan.Arguments);
        Assert.Contains("--timeout", plan.Arguments);
        Assert.Contains("60", plan.Arguments);
        Assert.Contains("--load-delay", plan.Arguments);
        Assert.Contains("2000", plan.Arguments);
        Assert.Contains("--no-sandbox", plan.Arguments);
        Assert.Contains("--no-reporter", plan.Arguments);
    }

    // ============== ConvertToSarif verb ==============

    [Fact]
    public void ConvertToSarif_Positional_Input_Then_Output()
    {
        var plan = AxeCore.ConvertToSarif(s => s
            .SetBinary(FakeConverterBinary())
            .SetInputFile("axe.json")
            .SetOutputFile("axe.sarif"));

        Assert.Equal("axe.json", plan.Arguments[0]);
        Assert.Equal("axe.sarif", plan.Arguments[1]);
    }

    [Fact]
    public void ConvertToSarif_Without_Input_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AxeCore.ConvertToSarif(s => s
            .SetBinary(FakeConverterBinary())
            .SetOutputFile("axe.sarif")));
        Assert.Contains("InputFile", ex.Message);
    }

    [Fact]
    public void ConvertToSarif_Without_Output_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AxeCore.ConvertToSarif(s => s
            .SetBinary(FakeConverterBinary())
            .SetInputFile("axe.json")));
        Assert.Contains("OutputFile", ex.Message);
    }

    [Fact]
    public void ConvertToSarif_Binary_PrefixArguments_Lead_The_Arg_List()
    {
        var binary = new AxeCoreBinaryResolution
        {
            Executable = "/usr/local/bin/npm",
            PrefixArguments = new[] { "exec", "axe-sarif-converter", "--" },
            Source = AxeCoreResolutionSource.Npm,
        };

        var plan = AxeCore.ConvertToSarif(s => s
            .SetBinary(binary)
            .SetInputFile("axe.json")
            .SetOutputFile("axe.sarif"));

        Assert.Equal("/usr/local/bin/npm", plan.Executable);
        Assert.Equal(new[] { "exec", "axe-sarif-converter", "--" }, plan.Arguments.Take(3).ToArray());
        Assert.Equal("axe.json", plan.Arguments[3]);
        Assert.Equal("axe.sarif", plan.Arguments[4]);
    }

    [Fact]
    public void ConvertToSarif_Working_Directory_And_Env_Propagate()
    {
        var plan = AxeCore.ConvertToSarif(s => s
            .SetBinary(FakeConverterBinary())
            .SetInputFile("axe.json")
            .SetOutputFile("axe.sarif")
            .SetWorkingDirectory("/work/web")
            .SetEnvironmentVariable("NODE_ENV", "production"));
        Assert.Equal("/work/web", plan.WorkingDirectory);
        Assert.Equal("production", plan.Environment["NODE_ENV"]);
    }

    [Fact]
    public void ConvertToSarif_ObjectInit_Identical_To_Fluent()
    {
        var bin = FakeConverterBinary();
        var fluent = AxeCore.ConvertToSarif(s => s
            .SetBinary(bin)
            .SetInputFile("axe.json")
            .SetOutputFile("axe.sarif"));
        var objInit = AxeCore.ConvertToSarif(new AxeCoreSarifConvertSettings
        {
            Binary = bin,
            InputFile = "axe.json",
            OutputFile = "axe.sarif",
        });
        Assert.Equal(fluent.Arguments, objInit.Arguments);
        Assert.Equal(fluent.Executable, objInit.Executable);
    }

    // ---- Boundary fuzz ----

    [Theory]
    [InlineData("https://example.com/path with spaces")]
    [InlineData("https://example.com/Δ-π")]
    [InlineData("https://example.com/sub'quote")]
    public void Url_Roundtrips_Verbatim(string url)
    {
        var plan = AxeCore.Scan(s => s
            .SetBinary(FakeAxeBinary()).AddUrl(url).SetOutputFile("out.json"));
        Assert.Equal(url, plan.Arguments[0]);
    }

    [Fact]
    public void Bulk_Rules_And_Tags_All_Emit_Joined()
    {
        var faker = new Faker();
        var rules = Enumerable.Range(0, 15)
            .Select(_ => faker.Hacker.Noun() + "-rule")
            .Distinct()
            .ToList();
        var tags = new[] { "wcag2a", "wcag2aa", "wcag21aa", "wcag22aa", "best-practice" };

        var plan = AxeCore.Scan(s =>
        {
            s.SetBinary(FakeAxeBinary()).AddUrl("https://x").SetOutputFile("out.json");
            foreach (var r in rules) s.AddRule(r);
            foreach (var t in tags) s.AddTag(t);
        });

        var rulesIdx = IndexOf(plan.Arguments, "--rules");
        var tagsIdx = IndexOf(plan.Arguments, "--tags");
        Assert.Equal(string.Join(",", rules), plan.Arguments[rulesIdx + 1]);
        Assert.Equal(string.Join(",", tags), plan.Arguments[tagsIdx + 1]);
    }
}
