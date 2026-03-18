using CalqFramework.Flow.Discovery;
using static CalqFramework.Cmd.Terminal;

namespace CalqFramework.Flow.Tests.Discovery;

public class TestProjectAssociationTest : IDisposable {
    private readonly string _bareRepo;
    private readonly string _workDir;

    public TestProjectAssociationTest() {
        TestHelper.SuppressLogging();
        _bareRepo = TestHelper.CreateBareRepo();
        _workDir = TestHelper.CreateWorkingRepo(_bareRepo);
    }

    public void Dispose() {
        TestHelper.CleanupDir(_bareRepo);
        TestHelper.CleanupDir(_workDir);
    }

    [Fact]
    public void FindTestProjects_FindsAssociatedTestProject() {
        var libProj = TestHelper.CreateProject(_workDir, "MyLib");
        // Create test project at sibling level
        var testDir = Path.Combine(_workDir, "MyLibTests");
        Directory.CreateDirectory(testDir);
        var testProj = Path.Combine(testDir, "MyLibTests.csproj");
        File.WriteAllText(testProj,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>");

        TestHelper.CommitAndPush(_workDir, "init");

        var prev = PWD;
        CD(_workDir);
        var repoRoot = CMD("git rev-parse --show-toplevel").Trim();
        CD(prev);

        var result = TestProjectAssociation.FindTestProjects(
            new List<string> { libProj }, repoRoot);

        Assert.Single(result);
        Assert.Contains("MyLibTest", Path.GetFileName(result[libProj]));
    }

    [Fact]
    public void FindTestProjects_ReturnsEmptyWhenNoTestProject() {
        var libProj = TestHelper.CreateProject(_workDir, "MyLib");
        TestHelper.CommitAndPush(_workDir, "init");

        var prev = PWD;
        CD(_workDir);
        var repoRoot = CMD("git rev-parse --show-toplevel").Trim();
        CD(prev);

        var result = TestProjectAssociation.FindTestProjects(
            new List<string> { libProj }, repoRoot);

        Assert.Empty(result);
    }
}
