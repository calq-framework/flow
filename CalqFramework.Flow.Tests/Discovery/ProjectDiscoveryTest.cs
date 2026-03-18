using CalqFramework.Flow.Discovery;

namespace CalqFramework.Flow.Tests.Discovery;

public class ProjectDiscoveryTest : IDisposable {
    private readonly string _tempDir;

    public ProjectDiscoveryTest() {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-test-disc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() {
        TestHelper.CleanupDir(_tempDir);
    }

    [Fact]
    public void DiscoverProjects_FindsCsproj() {
        TestHelper.CreateProject(_tempDir, "MyLib");

        var projects = ProjectDiscovery.DiscoverProjects(_tempDir);

        Assert.Single(projects);
        Assert.Contains("MyLib.csproj", projects[0]);
    }

    [Fact]
    public void DiscoverProjects_ExcludesTestProjects() {
        TestHelper.CreateProject(_tempDir, "MyLib");
        TestHelper.CreateProject(_tempDir, "MyLibTests");
        TestHelper.CreateProject(_tempDir, "MyLibTest");

        var projects = ProjectDiscovery.DiscoverProjects(_tempDir);

        Assert.Single(projects);
        Assert.Contains("MyLib.csproj", projects[0]);
    }

    [Fact]
    public void DiscoverProjects_ExcludesExampleProjects() {
        TestHelper.CreateProject(_tempDir, "MyLib");
        TestHelper.CreateProject(_tempDir, "MyLibExample");
        TestHelper.CreateProject(_tempDir, "MyLibSample");

        var projects = ProjectDiscovery.DiscoverProjects(_tempDir);

        Assert.Single(projects);
        Assert.Contains("MyLib.csproj", projects[0]);
    }

    [Fact]
    public void DiscoverProjects_ExcludesNestedProjects() {
        // Parent project
        TestHelper.CreateProject(_tempDir, "Parent");
        // Nested project inside Parent's directory
        var parentDir = Path.Combine(_tempDir, "Parent");
        var nestedDir = Path.Combine(parentDir, "Nested");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(nestedDir, "Nested.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>");

        var projects = ProjectDiscovery.DiscoverProjects(_tempDir);

        Assert.Single(projects);
        Assert.Contains("Parent.csproj", projects[0]);
    }

    [Fact]
    public void DiscoverProjects_FindsMultipleNonNestedProjects() {
        TestHelper.CreateProject(_tempDir, "LibA");
        TestHelper.CreateProject(_tempDir, "LibB");

        var projects = ProjectDiscovery.DiscoverProjects(_tempDir);

        Assert.Equal(2, projects.Count);
    }
}
