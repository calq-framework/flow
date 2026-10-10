using CalqFramework.Flow.Diff;

namespace CalqFramework.Flow.Tests.Diff;

/// <summary>
///     Regression tests for the CI crash where SyntacticVersioning.Compare threw an
///     unhandled TypeLoadException: the base assembly referenced a dependency type by
///     its old full name (type later moved to a different namespace), and the base
///     resolver fell back to the current build's siblings for resolution.
/// </summary>
public class SyntacticDiffMovedTypeTest {
    private const string DepV1Source = """
        namespace Ns;
        public class DepEx : System.Exception {
            public DepEx(string message) : base(message) { }
        }
        """;

    private const string DepV2Source = """
        namespace Ns.Sub;
        public class DepEx : System.Exception {
            public DepEx(string message) : base(message) { }
        }
        """;

    private static string ClientSource(string ns) => """
        global using NSPLACEHOLDER;
        namespace Client;
        public class Work {
            public string Handle(DepEx ex) => ex.Message;
            public string Handle(int n) => n.ToString();
        }
        """.Replace("NSPLACEHOLDER", ns);

    [Fact]
    public void Compare_DoesNotThrow_WhenBaseReferencesMovedType() {
        TestHelper.SuppressLogging();
        string root = Path.Combine(Path.GetTempPath(), $"flow-diff-moved-type-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try {
            string clientBaseDir = BuildProject(root, "ClientBase", ClientSource("Ns"), DepV1Source);
            string clientCurrentDir = BuildProject(root, "ClientCurrent", ClientSource("Ns.Sub"), DepV2Source);

            // Base directory contains only the client DLL (like a NuGet cache package
            // folder without dependency assemblies), so the base resolver falls back
            // to the current build's siblings  Ewhere Ns.DepEx no longer exists.
            string isolatedBaseDir = Path.Combine(root, "base-only");
            Directory.CreateDirectory(isolatedBaseDir);
            string baseDll = Directory.GetFiles(clientBaseDir, "ClientBase.dll", SearchOption.AllDirectories)
                .First();
            File.Copy(baseDll, Path.Combine(isolatedBaseDir, "ClientBase.dll"));
            string currentDll = Directory.GetFiles(clientCurrentDir, "ClientCurrent.dll", SearchOption.AllDirectories)
                .First();

            ProjectDiffResult result = SyntacticVersioning.Compare("ClientCurrent", currentDll, Path.Combine(isolatedBaseDir, "ClientBase.dll"), false);

            Assert.NotNull(result);
            Assert.True(result.Changes.Count > 0);
        } finally {
            TestHelper.CleanupDir(root);
        }
    }

    private static string BuildProject(string root, string projectName, string clientSource,  string depSource) {
        string projectDir = Path.Combine(root, projectName);
        Directory.CreateDirectory(projectDir);

        string depProjectDir = Path.Combine(root, $"{projectName}.Dep");
        Directory.CreateDirectory(depProjectDir);
        File.WriteAllText(Path.Combine(depProjectDir, "Dep.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <AssemblyName>Dep</AssemblyName>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(depProjectDir, "Dep.cs"), depSource);
        CMD($"dotnet build \"{Path.Combine(depProjectDir, "Dep.csproj")}\" -c Release");
        string depDll = Path.Combine(depProjectDir, "bin", "Release", "net10.0", "Dep.dll");

        File.WriteAllText(Path.Combine(projectDir, $"{projectName}.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <AssemblyName>{projectName}</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="Dep"><HintPath>{depDll}</HintPath></Reference>
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(projectDir, $"{projectName}.cs"), clientSource);
        CMD($"dotnet build \"{Path.Combine(projectDir, $"{projectName}.csproj")}\" -c Release");
        return projectDir;
    }
}
