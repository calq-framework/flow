namespace CalqFramework.Flow.Diff;

/// <summary>
///     DTOs for member-level change metadata (§8).
/// </summary>
public class ProjectDiffResult {
    public string ProjectName { get; set; } = "";
    public List<MemberChange> Changes { get; set; } = [];
    public bool HasBreakingChanges => Changes.Any(c => c.IsBreaking);
    public bool HasNonBreakingChanges => Changes.Any(c => !c.IsBreaking);
    public bool ByteLevelFallback { get; set; }
}

public class MemberChange {
    public string MemberIdentity { get; set; } = "";
    public ChangeKind Kind { get; set; }
    public bool IsBreaking => Kind is ChangeKind.Deleted or ChangeKind.AttributeModified;
}

public enum ChangeKind {
    /// <summary>Member was added (non-breaking).</summary>
    Added,

    /// <summary>Member was deleted (breaking).</summary>
    Deleted,

    /// <summary>Member attributes were modified (breaking).</summary>
    AttributeModified,

    /// <summary>IL bytecode changed but signature is the same (non-breaking).</summary>
    ILChanged
}
