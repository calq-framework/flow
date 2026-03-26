using CalqFramework.Flow.Versioning;

namespace CalqFramework.Flow.Tests.Versioning;

public class VersionResolverTest : IDisposable {
    private readonly string _bareRepo;
    private readonly string _workDir;

    public VersionResolverTest() {
        TestHelper.SuppressLogging();
        _bareRepo = TestHelper.CreateBareRepo();
        _workDir = TestHelper.CreateWorkingRepo(_bareRepo);
    }

    public void Dispose() {
        TestHelper.CleanupDir(_bareRepo);
        TestHelper.CleanupDir(_workDir);
    }

    [Fact]
    public void ResolveLatestTagVersion_FindsHighestTag() {
        TestHelper.CreateProject(_workDir, "Lib", "1.0.0");
        TestHelper.CommitAndPush(_workDir, "v1");
        TestHelper.TagAndPush(_workDir, "v1.0.0");

        // Make another commit and tag
        File.WriteAllText(Path.Combine(_workDir, "Lib", "dummy.txt"), "change");
        TestHelper.CommitAndPush(_workDir, "v2");
        TestHelper.TagAndPush(_workDir, "v1.1.0");

        string prev = PWD;
        CD(_workDir);
        Version? version = VersionResolver.ResolveLatestTagVersion("origin", "v");
        CD(prev);

        Assert.NotNull(version);
        Assert.Equal(new Version(1, 1, 0), version);
    }

    [Fact]
    public void ResolveLatestTagVersion_ReturnsNullWhenNoTags() {
        TestHelper.CreateProject(_workDir, "Lib");
        TestHelper.CommitAndPush(_workDir, "init");

        string prev = PWD;
        CD(_workDir);
        Version? version = VersionResolver.ResolveLatestTagVersion("origin", "v");
        CD(prev);

        Assert.Null(version);
    }

    [Fact]
    public void ReadVersionFromProject_ReadsVersionElement() {
        string proj = TestHelper.CreateProject(_workDir, "Lib", "2.3.4");

        Version? version = VersionResolver.ReadVersionFromProject(proj);

        Assert.NotNull(version);
        Assert.Equal(new Version(2, 3, 4), version);
    }

    [Fact]
    public void ReadVersionFromProject_ReadsVersionPrefix() {
        string projDir = Path.Combine(_workDir, "PrefixLib");
        Directory.CreateDirectory(projDir);
        string projPath = Path.Combine(projDir, "PrefixLib.csproj");
        File.WriteAllText(
            projPath,
            @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <VersionPrefix>3.2.1</VersionPrefix>
    <VersionSuffix>beta</VersionSuffix>
  </PropertyGroup>
</Project>");

        Version? version = VersionResolver.ReadVersionFromProject(projPath);

        Assert.NotNull(version);
        Assert.Equal(new Version(3, 2, 1), version);
    }

    [Fact]
    public void ReadVersionFromProject_StripsPrereleaseSuffix() {
        string projDir = Path.Combine(_workDir, "SuffixLib");
        Directory.CreateDirectory(projDir);
        string projPath = Path.Combine(projDir, "SuffixLib.csproj");
        File.WriteAllText(
            projPath,
            @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Version>1.2.3-rc.1</Version>
  </PropertyGroup>
</Project>");

        Version? version = VersionResolver.ReadVersionFromProject(projPath);

        Assert.NotNull(version);
        Assert.Equal(new Version(1, 2, 3), version);
    }

    [Fact]
    public void ResolveProjectVersions_MapsAllProjects() {
        string proj1 = TestHelper.CreateProject(_workDir, "LibA", "1.0.0");
        string proj2 = TestHelper.CreateProject(_workDir, "LibB", "2.0.0");

        Dictionary<string, Version> versions = VersionResolver.ResolveProjectVersions(
        [
            proj1,
            proj2
        ]);

        Assert.Equal(2, versions.Count);
        Assert.Equal(new Version(1, 0, 0), versions[proj1]);
        Assert.Equal(new Version(2, 0, 0), versions[proj2]);
    }
}
