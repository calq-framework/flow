namespace CalqFramework.Flow.Tests;

public class FlowManagerTest : IDisposable {
    private readonly string _bareRepo;
    private readonly string _nugetFeed;
    private readonly string _workDir;

    public FlowManagerTest() {
        TestHelper.SuppressLogging();
        _bareRepo = TestHelper.CreateBareRepo();
        _workDir = TestHelper.CreateWorkingRepo(_bareRepo);
        _nugetFeed = Path.Combine(Path.GetTempPath(), $"flow-test-feed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_nugetFeed);
    }

    public void Dispose() {
        TestHelper.CleanupDir(_bareRepo);
        TestHelper.CleanupDir(_workDir);
        TestHelper.CleanupDir(_nugetFeed);
    }

    [Fact]
    public void Publish_DryRun_ReturnsResultWithoutMutatingState() {
        // Setup: create a project, commit, tag v1.0.0
        TestHelper.CreateProject(_workDir, "MyLib", "1.0.0");
        TestHelper.CreateSourceFile(Path.Combine(_workDir, "MyLib"), "Greeter", "namespace MyLib; public class Greeter { public string Greet() => \"hello\"; }");
        TestHelper.CommitAndPush(_workDir, "initial");
        TestHelper.TagAndPush(_workDir, "v1.0.0");

        // Make a change after the tag
        File.WriteAllText(Path.Combine(_workDir, "MyLib", "Greeter.cs"), "namespace MyLib; public class Greeter { public string Greet() => \"hello v2\"; }");
        string prev = PWD;
        CD(_workDir);
        CMD("git add -A");
        CMD("git commit -m \"breaking change\"");
        CD(prev);

        // Execute dry-run
        FlowManager flow = new() {
            Sources = [
                _nugetFeed
            ],
            Remote = "origin",
            TagPrefix = "v"
        };

        CD(_workDir);
        PublishResult result = flow.Publish(true);
        CD(prev);

        // Verify result
        Assert.True(result.DryRun);
        Assert.Equal("1.0.0", result.PreviousVersion);
        Assert.NotEmpty(result.ChangedProjects);
        Assert.Contains("MyLib", result.ChangedProjects);

        // Verify no new tags were created (dry-run)
        CD(_workDir);
        string tags = CMD("git ls-remote --tags origin");
        CD(prev);
        Assert.DoesNotContain("v1.1.0", tags);
        Assert.DoesNotContain("v1.0.1", tags);
    }

    [Fact]
    public void Publish_DryRun_DefaultsSourceToMain() {
        TestHelper.CreateProject(_workDir, "MyLib", "1.0.0");
        TestHelper.CreateSourceFile(Path.Combine(_workDir, "MyLib"), "Foo");
        TestHelper.CommitAndPush(_workDir, "init");

        FlowManager flow = new() {
            Remote = "origin",
            TagPrefix = "v"
            // Sources left empty — should default to ["main"]
        };

        string prev = PWD;
        CD(_workDir);
        PublishResult result = flow.Publish(true);
        CD(prev);

        Assert.Equal(
            [
                "main"
            ],
            flow.Sources);
    }

    [Fact]
    public void Publish_DryRun_NoChanges_ReturnsEmptyResult() {
        TestHelper.CreateProject(_workDir, "MyLib", "1.0.0");
        TestHelper.CreateSourceFile(Path.Combine(_workDir, "MyLib"), "Foo");
        TestHelper.CommitAndPush(_workDir, "init");
        TestHelper.TagAndPush(_workDir, "v1.0.0");

        // No changes after the tag

        FlowManager flow = new() {
            Sources = [
                _nugetFeed
            ],
            Remote = "origin",
            TagPrefix = "v"
        };

        string prev = PWD;
        CD(_workDir);
        PublishResult result = flow.Publish(true);
        CD(prev);

        Assert.True(result.DryRun);
        Assert.Empty(result.ChangedProjects);
        Assert.Empty(result.PublishedPackages);
        Assert.Equal("1.0.0", result.PreviousVersion);
        Assert.Equal("1.0.0", result.TargetVersion);
    }
}
