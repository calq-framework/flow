using CalqFramework.Flow.Diff;

namespace CalqFramework.Flow.Tests.Diff;

public class SyntacticDiffTest {
    [Fact]
    public void MemberChange_DeletedIsBreaking() {
        MemberChange change = new() {
            Kind = ChangeKind.Deleted
        };
        Assert.True(change.IsBreaking);
    }

    [Fact]
    public void MemberChange_AttributeModifiedIsBreaking() {
        MemberChange change = new() {
            Kind = ChangeKind.AttributeModified
        };
        Assert.True(change.IsBreaking);
    }

    [Fact]
    public void MemberChange_AddedIsNotBreaking() {
        MemberChange change = new() {
            Kind = ChangeKind.Added
        };
        Assert.False(change.IsBreaking);
    }

    [Fact]
    public void MemberChange_ILChangedIsNotBreaking() {
        MemberChange change = new() {
            Kind = ChangeKind.ILChanged
        };
        Assert.False(change.IsBreaking);
    }

    [Fact]
    public void ProjectDiffResult_HasBreakingChanges_WhenDeletedMember() {
        ProjectDiffResult result = new() {
            ProjectName = "Lib",
            Changes = {
                new MemberChange {
                    MemberIdentity = "Foo",
                    Kind = ChangeKind.Added
                },
                new MemberChange {
                    MemberIdentity = "Bar",
                    Kind = ChangeKind.Deleted
                }
            }
        };

        Assert.True(result.HasBreakingChanges);
        Assert.True(result.HasNonBreakingChanges);
    }

    [Fact]
    public void ProjectDiffResult_NoBreakingChanges_WhenOnlyAdded() {
        ProjectDiffResult result = new() {
            ProjectName = "Lib",
            Changes = {
                new MemberChange {
                    MemberIdentity = "Foo",
                    Kind = ChangeKind.Added
                }
            }
        };

        Assert.False(result.HasBreakingChanges);
        Assert.True(result.HasNonBreakingChanges);
    }
}
