namespace CalqFramework.Flow.Git;

/// <summary>
///     Creates global tags and updates rolling branch pointers (§12).
/// </summary>
public static class TaggingStrategy {
    /// <summary>
    ///     Creates a global version tag: {prefix}{version}.
    /// </summary>
    public static void CreateTag(string tagPrefix, Version version, string remote) {
        string tag = $"{tagPrefix}{version.ToString(3)}";
        RUN($"git tag {tag}");
        RUN($"git push {remote} {tag}");
    }

    /// <summary>
    ///     Force-updates a rolling branch pointer to the current HEAD (§12).
    /// </summary>
    public static void UpdateRollingBranch(string branchName, string remote) {
        string commitHash = GitOperations.GetHeadCommitHash();
        RUN($"git push {remote} {commitHash}:refs/heads/{branchName} --force");
    }
}
