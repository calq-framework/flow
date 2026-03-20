using CalqFramework.Flow.Git;

namespace CalqFramework.Flow.Tests.Git;

public class GitOperationsTest : IDisposable {
    private readonly string _bareRepo;
    private readonly string _workDir;

    public GitOperationsTest() {
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
    public void GetRepositoryRoot_ReturnsRepoRoot() {
        string prev = PWD;
        CD(_workDir);
        string root = GitOperations.GetRepositoryRoot(_workDir);
        CD(prev);

        Assert.NotEmpty(root);
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public void GetHeadCommitHash_Returns40CharHash() {
        string prev = PWD;
        CD(_workDir);
        string hash = GitOperations.GetHeadCommitHash();
        CD(prev);

        Assert.Equal(40, hash.Length);
        Assert.Matches("^[0-9a-f]{40}$", hash);
    }
}
