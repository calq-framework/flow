using CalqFramework.Flow.Diff;

namespace CalqFramework.Flow.Tests.Diff;

public class SyntacticDiffTest {
    [Fact]
    public void MemberChange_DeletedIsBreaking() {
        var change = new MemberChange { Kind = ChangeKind.Deleted };
        Assert.True(change.IsBreaking);
    }

    [Fact]
    public void MemberChange_AttributeModifiedIsBreaking() {
        var change = new MemberChange { Kind = ChangeKind.AttributeModified };
        Assert.True(change.IsBreaking);
    }

    [Fact]
    public void MemberChange_AddedIsNotBreaking() {
        var change = new MemberChange { Kind = ChangeKind.Added };
        Assert.False(change.IsBreaking);
    }

    [Fact]
    public void MemberChange_ILChangedIsNotBreaking() {
        var change = new MemberChange { Kind = ChangeKind.ILChanged };
        Assert.False(change.IsBreaking);
    }

    [Fact]
    public void ProjectDiffResult_HasBreakingChanges_WhenDeletedMember() {
        var result = new ProjectDiffResult {
            ProjectName = "Lib",
            Changes = {
                new MemberChange { MemberIdentity = "Foo", Kind = ChangeKind.Added },
                new MemberChange { MemberIdentity = "Bar", Kind = ChangeKind.Deleted }
            }
        };

        Assert.True(result.HasBreakingChanges);
        Assert.True(result.HasNonBreakingChanges);
    }

    [Fact]
    public void ProjectDiffResult_NoBreakingChanges_WhenOnlyAdded() {
        var result = new ProjectDiffResult {
            ProjectName = "Lib",
            Changes = {
                new MemberChange { MemberIdentity = "Foo", Kind = ChangeKind.Added }
            }
        };

        Assert.False(result.HasBreakingChanges);
        Assert.True(result.HasNonBreakingChanges);
    }
}
