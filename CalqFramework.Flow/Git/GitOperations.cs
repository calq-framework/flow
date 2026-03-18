using static CalqFramework.Cmd.Terminal;

namespace CalqFramework.Flow.Git;

/// <summary>
/// Common Git operations used across the tool.
/// </summary>
public static class GitOperations {
    /// <summary>
    /// Returns the root directory of the Git repository.
    /// </summary>
    public static string GetRepositoryRoot(string workingDirectory) {
        return CMD("git rev-parse --show-toplevel").Trim();
    }

    /// <summary>
    /// Returns the current HEAD commit hash.
    /// </summary>
    public static string GetHeadCommitHash() {
        return CMD("git rev-parse HEAD").Trim();
    }
}
