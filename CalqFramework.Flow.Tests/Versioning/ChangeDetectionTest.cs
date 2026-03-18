using CalqFramework.Flow.Versioning;
using static CalqFramework.Cmd.Terminal;

namespace CalqFramework.Flow.Tests.Versioning;

public class ChangeDetectionTest : IDisposable {
    private readonly string _bareRepo;
    private readonly string _workDir;

    public ChangeDetectionTest() {
        TestHelper.SuppressLogging();
        _bareRepo = TestHelper.CreateBareRepo();
        _workDir = TestHelper.CreateWorkingRepo(_bareRepo);
    }

    public void Dispose() {
        TestHelper.CleanupDir(_bareRepo);
        TestHelper.CleanupDir(_workDir);
    }

    [Fact]
    public void DetectChangedProjects_DetectsChangeInProjectDir() {
        var proj = TestHelper.CreateProject(_workDir, "MyLib");
        TestHelper.CreateSourceFile(Path.Combine(_workDir, "MyLib"), "Foo");
        TestHelper.CommitAndPush(_workDir, "init");
        TestHelper.TagAndPush(_workDir, "v1.0.0");

        // Modify a file inside the project directory
        File.WriteAllText(Path.Combine(_workDir, "MyLib", "Foo.cs"),
            "namespace TestLib; public class Foo { public int Value => 42; }");
        var prev = PWD;
        CD(_workDir);
        CMD("git add -A");
        CMD("git commit -m \"change\"");
        CD(prev);

        CD(_workDir);
        var changed = ChangeDetection.DetectChangedProjects(
            new List<string> { proj }, "origin", "v");
        CD(prev);

        Assert.Single(changed);
        Assert.Equal(proj, changed[0]);
    }

    [Fact]
    public void DetectChangedProjects_IgnoresRootFileChanges() {
        var proj = TestHelper.CreateProject(_workDir, "MyLib");
        TestHelper.CreateSourceFile(Path.Combine(_workDir, "MyLib"), "Foo");
        TestHelper.CommitAndPush(_workDir, "init");
        TestHelper.TagAndPush(_workDir, "v1.0.0");

        // Modify a root-level file (not inside any project dir)
        File.WriteAllText(Path.Combine(_workDir, "README.md"), "# Hello");
        var prev = PWD;
        CD(_workDir);
        CMD("git add -A");
        CMD("git commit -m \"root change\"");
        CD(prev);

        CD(_workDir);
        var changed = ChangeDetection.DetectChangedProjects(
            new List<string> { proj }, "origin", "v");
        CD(prev);

        Assert.Empty(changed);
    }

    [Fact]
    public void DetectChangedProjects_DetectsSubdirectoryChanges() {
        var proj = TestHelper.CreateProject(_workDir, "MyLib");
        var subDir = Path.Combine(_workDir, "MyLib", "SubFolder");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "Helper.cs"),
            "namespace TestLib; public class Helper { }");
        TestHelper.CommitAndPush(_workDir, "init");
        TestHelper.TagAndPush(_workDir, "v1.0.0");

        // Modify a file in a subdirectory of the project
        File.WriteAllText(Path.Combine(subDir, "Helper.cs"),
            "namespace TestLib; public class Helper { public int X => 1; }");
        var prev = PWD;
        CD(_workDir);
        CMD("git add -A");
        CMD("git commit -m \"sub change\"");
        CD(prev);

        CD(_workDir);
        var changed = ChangeDetection.DetectChangedProjects(
            new List<string> { proj }, "origin", "v");
        CD(prev);

        Assert.Single(changed);
    }
}
