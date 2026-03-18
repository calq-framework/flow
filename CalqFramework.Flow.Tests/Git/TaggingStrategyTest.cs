using CalqFramework.Flow.Git;
using static CalqFramework.Cmd.Terminal;

namespace CalqFramework.Flow.Tests.Git;

public class TaggingStrategyTest : IDisposable {
    private readonly string _bareRepo;
    private readonly string _workDir;

    public TaggingStrategyTest() {
        TestHelper.SuppressLogging();
        _bareRepo = TestHelper.CreateBareRepo();
        _workDir = TestHelper.CreateWorkingRepo(_bareRepo);
        TestHelper.CreateProject(_workDir, "Lib");
        TestHelper.CommitAndPush(_workDir, "init");
    }

    public void Dispose() {
        TestHelper.CleanupDir(_bareRepo);
        TestHelper.CleanupDir(_workDir);
    }

    [Fact]
    public void CreateTag_PushesTagToRemote() {
        var prev = PWD;
        CD(_workDir);
        TaggingStrategy.CreateTag("v", new Version(1, 2, 3), "origin");

        // Verify tag exists on remote
        var tags = CMD("git ls-remote --tags origin");
        CD(prev);

        Assert.Contains("v1.2.3", tags);
    }

    [Fact]
    public void UpdateRollingBranch_ForceUpdatesRemoteBranch() {
        var prev = PWD;
        CD(_workDir);
        TaggingStrategy.UpdateRollingBranch("latest", "origin");

        // Verify the branch exists on remote
        var refs = CMD("git ls-remote --heads origin");
        CD(prev);

        Assert.Contains("refs/heads/latest", refs);
    }
}
