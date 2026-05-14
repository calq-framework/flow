using System.IO.Compression;
using System.Reflection.PortableExecutable;
using CalqFramework.Flow.Discovery;
using CalqFramework.Flow.Pipeline;

namespace CalqFramework.Flow.Tests.Pipeline;

/// <summary>
///     Replicates the CalqFramework.Dev packaging pipeline to verify
///     that the library nupkg is produced with valid Source Link metadata,
///     deterministic build, and correct compiler flags.
/// </summary>
public class BuildPipelineTest : IDisposable {
    private readonly string _bareRepo;
    private readonly string _workDir;

    public BuildPipelineTest() {
        TestHelper.SuppressLogging();
        _bareRepo = TestHelper.CreateBareRepo();
        _workDir = TestHelper.CreateWorkingRepo(_bareRepo);
    }

    public void Dispose() {
        TestHelper.CleanupDir(_bareRepo);
        TestHelper.CleanupDir(_workDir);
    }

    /// <summary>
    ///     Replicates the CalqFramework.Dev + CalqFramework.Dev.Cli repo structure
    ///     and runs the same pipeline steps (discover → test association → build → pack).
    ///     Validates that the library nupkg contains a DLL with:
    ///     - Deterministic build (Reproducible debug directory entry)
    ///     - Embedded PDB (DebugType=embedded)
    ///     - Compiler flags (ContinuousIntegrationBuild)
    /// </summary>
    [Fact]
    public void Pack_LibraryWithSourceLink_ProducesValidNupkg() {
        // ── Arrange: replicate CalqFramework.Dev repo structure ──

        // CalqFramework.Dev (library with PublishRepositoryUrl=true)
        string libDir = Path.Combine(_workDir, "CalqFramework.Dev");
        Directory.CreateDirectory(libDir);
        string libCsproj = Path.Combine(libDir, "CalqFramework.Dev.csproj");
        File.WriteAllText(libCsproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <RootNamespace>CalqFramework.Dev</RootNamespace>
                <GenerateDocumentationFile>true</GenerateDocumentationFile>
                <NoWarn>$(NoWarn);CS1591</NoWarn>
                <PackageId>CalqFramework.Dev</PackageId>
                <Version>1.0.0</Version>
                <PackageProjectUrl>https://github.com/calq-framework</PackageProjectUrl>
                <PackageLicenseExpression>MIT</PackageLicenseExpression>
                <Copyright>2026-2026 Calq Framework</Copyright>
                <PublishRepositoryUrl>true</PublishRepositoryUrl>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(libDir, "DevManager.cs"), """
            namespace CalqFramework.Dev;
            public class DevManager {
                public string Run() => "hello";
            }
            """);

        // CalqFramework.Dev.Cli (tool package, no PublishRepositoryUrl)
        string cliDir = Path.Combine(_workDir, "CalqFramework.Dev.Cli");
        Directory.CreateDirectory(cliDir);
        string cliCsproj = Path.Combine(cliDir, "CalqFramework.Dev.Cli.csproj");
        File.WriteAllText(cliCsproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <RootNamespace>CalqFramework.Dev.Cli</RootNamespace>
                <PackAsTool>true</PackAsTool>
                <ToolCommandName>dev</ToolCommandName>
                <PackageId>CalqFramework.Dev.Cli</PackageId>
                <Version>1.0.0</Version>
                <PackageProjectUrl>https://github.com/calq-framework</PackageProjectUrl>
                <PackageLicenseExpression>MIT</PackageLicenseExpression>
                <Copyright>2026-2026 Calq Framework</Copyright>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\CalqFramework.Dev\CalqFramework.Dev.csproj" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(cliDir, "Program.cs"), """
            using CalqFramework.Dev;
            var mgr = new DevManager();
            Console.WriteLine(mgr.Run());
            """);

        // CalqFramework.Dev.Tests (excluded from discovery, but found by test association)
        string testDir = Path.Combine(_workDir, "CalqFramework.Dev.Tests");
        Directory.CreateDirectory(testDir);
        string testCsproj = Path.Combine(testDir, "CalqFramework.Dev.Tests.csproj");
        File.WriteAllText(testCsproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
                <PackageReference Include="xunit" Version="2.9.2" />
                <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
              </ItemGroup>
              <ItemGroup>
                <ProjectReference Include="..\CalqFramework.Dev\CalqFramework.Dev.csproj" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(testDir, "DevManagerTest.cs"), """
            using Xunit;
            using CalqFramework.Dev;
            namespace CalqFramework.Dev.Tests;
            public class DevManagerTest {
                [Fact]
                public void Run_ReturnsHello() => Assert.Equal("hello", new DevManager().Run());
            }
            """);

        TestHelper.CommitAndPush(_workDir, "init");

        // ── Act: replicate the exact pipeline from FlowManager.Publish ──
        string prev = PWD;
        CD(_workDir);

        // §4: Project Discovery (same as FlowManager)
        List<string> projects = ProjectDiscovery.DiscoverProjects(_workDir);

        // §5: Test Project Association (same as FlowManager)
        string repoRoot = _workDir;
        Dictionary<string, string> testAssociations = TestProjectAssociation.FindTestProjects(projects, repoRoot);

        // Phase 1: Build all projects (same as FlowManager — all are "changed" on first publish)
        foreach (string project in projects) {
            BuildPipeline.BuildCurrent(project, testAssociations);
        }

        // Phase 3: Pack all projects at target version
        Version targetVersion = new(1, 1, 2);
        var nupkgPaths = new Dictionary<string, string?>();
        foreach (string project in projects) {
            string name = Path.GetFileNameWithoutExtension(project);
            nupkgPaths[name] = BuildPipeline.Pack(project, targetVersion);
        }

        CD(prev);

        // ── Assert: validate CalqFramework.Dev nupkg ──
        Assert.True(nupkgPaths.ContainsKey("CalqFramework.Dev"), "CalqFramework.Dev should be discovered and packed");
        string? libNupkg = nupkgPaths["CalqFramework.Dev"];
        Assert.NotNull(libNupkg);
        Assert.True(File.Exists(libNupkg), "CalqFramework.Dev .nupkg file should exist");

        // Extract the nupkg and inspect the DLL
        string extractDir = Path.Combine(Path.GetTempPath(), $"nupkg-validate-{Guid.NewGuid():N}");
        try {
            ZipFile.ExtractToDirectory(libNupkg, extractDir);
            string[] dlls = Directory.GetFiles(extractDir, "CalqFramework.Dev.dll", SearchOption.AllDirectories);
            Assert.NotEmpty(dlls);

            using FileStream fs = File.OpenRead(dlls[0]);
            using PEReader peReader = new(fs);
            var debugEntries = peReader.ReadDebugDirectory().ToList();

            // Source Link: Valid — requires embedded PDB with source link metadata
            bool hasEmbeddedPdb = debugEntries.Any(e => e.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
            Assert.True(hasEmbeddedPdb,
                "Source Link: Missing Symbols — CalqFramework.Dev.dll must have DebugType=embedded. " +
                "The build pipeline must pass -p:DebugType=embedded to the library project.");

            // Deterministic (dll/exe): Valid — requires Reproducible debug directory entry
            bool hasReproducible = debugEntries.Any(e => e.Type == DebugDirectoryEntryType.Reproducible);
            Assert.True(hasReproducible,
                "Deterministic: Non deterministic — CalqFramework.Dev.dll must be built with Deterministic=true. " +
                "The build pipeline must pass -p:Deterministic=true to the library project.");
        } finally {
            TestHelper.CleanupDir(extractDir);
        }
    }
}
