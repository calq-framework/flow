using CalqFramework.Flow.Git;

namespace CalqFramework.Flow.Tests.Git;

public class ShadowCopyTest : IDisposable {
    private readonly string _bareRepo;
    private readonly string _workDir;
    private string? _shadowPath;

    public ShadowCopyTest() {
        TestHelper.SuppressLogging();
        _bareRepo = TestHelper.CreateBareRepo();
        _workDir = TestHelper.CreateWorkingRepo(_bareRepo);
    }

    public void Dispose() {
        if (_shadowPath != null) {
            ShadowCopy.Cleanup(_shadowPath);
        }

        TestHelper.CleanupDir(_bareRepo);
        TestHelper.CleanupDir(_workDir);
    }

    [Fact]
    public void Create_CopiesFilesExcludingBinObjVs() {
        TestHelper.CreateProject(_workDir, "Lib");
        // Create bin/obj/.vs dirs that should be excluded
        Directory.CreateDirectory(Path.Combine(_workDir, "Lib", "bin"));
        File.WriteAllText(Path.Combine(_workDir, "Lib", "bin", "output.dll"), "fake");
        Directory.CreateDirectory(Path.Combine(_workDir, "Lib", "obj"));
        File.WriteAllText(Path.Combine(_workDir, "Lib", "obj", "cache.json"), "fake");
        Directory.CreateDirectory(Path.Combine(_workDir, ".vs"));
        File.WriteAllText(Path.Combine(_workDir, ".vs", "settings.json"), "fake");

        TestHelper.CommitAndPush(_workDir, "init");

        string prev = PWD;
        CD(_workDir);
        string repoRoot = CMD("git rev-parse --show-toplevel")
            .Trim();
        _shadowPath = ShadowCopy.Create(_workDir, repoRoot, "origin", "v");
        CD(prev);

        // Project file should be copied
        Assert.True(File.Exists(Path.Combine(_shadowPath, "Lib", "Lib.csproj")));
        // bin/obj/.vs should NOT be copied
        Assert.False(Directory.Exists(Path.Combine(_shadowPath, "Lib", "bin")));
        Assert.False(Directory.Exists(Path.Combine(_shadowPath, "Lib", "obj")));
        Assert.False(Directory.Exists(Path.Combine(_shadowPath, ".vs")));
    }

    [Fact]
    public void Create_ChecksOutBaseVersionWhenTagExists() {
        TestHelper.CreateProject(_workDir, "Lib");
        string sourceFile = Path.Combine(_workDir, "Lib", "Foo.cs");
        File.WriteAllText(sourceFile, "// v1");
        TestHelper.CommitAndPush(_workDir, "v1");
        TestHelper.TagAndPush(_workDir, "v1.0.0");

        // Make a new change
        File.WriteAllText(sourceFile, "// v2 - changed");
        TestHelper.CommitAndPush(_workDir, "v2");

        string prev = PWD;
        CD(_workDir);
        string repoRoot = CMD("git rev-parse --show-toplevel")
            .Trim();
        _shadowPath = ShadowCopy.Create(_workDir, repoRoot, "origin", "v");
        CD(prev);

        // Shadow copy should have the v1 content (base version)
        string shadowFile = Path.Combine(_shadowPath, "Lib", "Foo.cs");
        Assert.True(File.Exists(shadowFile));
        string content = File.ReadAllText(shadowFile);
        Assert.Equal("// v1", content);
    }

    [Fact]
    public void Cleanup_DeletesShadowDirectory() {
        string tempPath = Path.Combine(Path.GetTempPath(), $"flow-test-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);
        File.WriteAllText(Path.Combine(tempPath, "test.txt"), "data");

        ShadowCopy.Cleanup(tempPath);

        Assert.False(Directory.Exists(tempPath));
    }
}
