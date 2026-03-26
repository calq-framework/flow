using CalqFramework.Flow.Discovery;

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
        string libProj = TestHelper.CreateProject(_workDir, "MyLib");
        // Create test project at sibling level
        string testDir = Path.Combine(_workDir, "MyLibTests");
        Directory.CreateDirectory(testDir);
        string testProj = Path.Combine(testDir, "MyLibTests.csproj");
        File.WriteAllText(testProj, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        TestHelper.CommitAndPush(_workDir, "init");

        string prev = PWD;
        CD(_workDir);
        string repoRoot = CMD("git rev-parse --show-toplevel")
            .Trim();
        CD(prev);

        Dictionary<string, string> result = TestProjectAssociation.FindTestProjects(
            [
                libProj
            ],
            repoRoot);

        Assert.Single(result);
        Assert.Contains("MyLibTest", Path.GetFileName(result[libProj]));
    }

    [Fact]
    public void FindTestProjects_ReturnsEmptyWhenNoTestProject() {
        string libProj = TestHelper.CreateProject(_workDir, "MyLib");
        TestHelper.CommitAndPush(_workDir, "init");

        string prev = PWD;
        CD(_workDir);
        string repoRoot = CMD("git rev-parse --show-toplevel")
            .Trim();
        CD(prev);

        Dictionary<string, string> result = TestProjectAssociation.FindTestProjects(
            [
                libProj
            ],
            repoRoot);

        Assert.Empty(result);
    }
}
