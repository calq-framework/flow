using CalqFramework.Flow.Diff;
using CalqFramework.Flow.Versioning;

namespace CalqFramework.Flow.Tests.Versioning;

public class VersionBumperTest {
    [Fact]
    public void BreakingChange_BumpsMinor() {
        var diffs = new List<ProjectDiffResult> {
            new() {
                ProjectName = "Lib",
                Changes = { new MemberChange { MemberIdentity = "Foo", Kind = ChangeKind.Deleted } }
            }
        };

        var result = VersionBumper.ComputeTargetVersion(
            new Version(0, 5, 0), new Dictionary<string, Version>(), diffs);

        Assert.Equal(new Version(0, 6, 0), result);
    }

    [Fact]
    public void NonBreakingChange_BumpsPatch() {
        var diffs = new List<ProjectDiffResult> {
            new() {
                ProjectName = "Lib",
                Changes = { new MemberChange { MemberIdentity = "Bar", Kind = ChangeKind.Added } }
            }
        };

        var result = VersionBumper.ComputeTargetVersion(
            new Version(1, 2, 3), new Dictionary<string, Version>(), diffs);

        Assert.Equal(new Version(1, 2, 4), result);
    }

    [Fact]
    public void ILChange_BumpsPatch() {
        var diffs = new List<ProjectDiffResult> {
            new() {
                ProjectName = "Lib",
                ByteLevelFallback = true,
                Changes = { new MemberChange { MemberIdentity = "Lib", Kind = ChangeKind.ILChanged } }
            }
        };

        var result = VersionBumper.ComputeTargetVersion(
            new Version(1, 0, 0), new Dictionary<string, Version>(), diffs);

        Assert.Equal(new Version(1, 0, 1), result);
    }

    [Fact]
    public void ProjectVersionTakesPrecedence_WhenHigher() {
        var diffs = new List<ProjectDiffResult> {
            new() {
                ProjectName = "Lib",
                Changes = { new MemberChange { MemberIdentity = "Bar", Kind = ChangeKind.Added } }
            }
        };
        var projectVersions = new Dictionary<string, Version> {
            { "Lib.csproj", new Version(5, 0, 0) }
        };

        var result = VersionBumper.ComputeTargetVersion(
            new Version(1, 0, 0), projectVersions, diffs);

        // §14: Higher version wins — project says 5.0.0, syntactic says 1.0.1
        Assert.Equal(new Version(5, 0, 0), result);
    }

    [Fact]
    public void SyntacticVersionTakesPrecedence_WhenHigher() {
        var diffs = new List<ProjectDiffResult> {
            new() {
                ProjectName = "Lib",
                Changes = { new MemberChange { MemberIdentity = "Foo", Kind = ChangeKind.Deleted } }
            }
        };
        var projectVersions = new Dictionary<string, Version> {
            { "Lib.csproj", new Version(0, 1, 0) }
        };

        var result = VersionBumper.ComputeTargetVersion(
            new Version(0, 5, 0), projectVersions, diffs);

        // Syntactic: 0.6.0 > project: 0.1.0
        Assert.Equal(new Version(0, 6, 0), result);
    }

    [Fact]
    public void NullLatestTag_StartsFromZero() {
        var diffs = new List<ProjectDiffResult> {
            new() {
                ProjectName = "Lib",
                Changes = { new MemberChange { MemberIdentity = "Bar", Kind = ChangeKind.Added } }
            }
        };

        var result = VersionBumper.ComputeTargetVersion(
            null, new Dictionary<string, Version>(), diffs);

        Assert.Equal(new Version(0, 0, 1), result);
    }
}
