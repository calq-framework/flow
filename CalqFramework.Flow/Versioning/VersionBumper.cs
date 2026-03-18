using CalqFramework.Flow.Diff;

namespace CalqFramework.Flow.Versioning;

/// <summary>
/// Computes the target version based on syntactic diff results (§9, §14).
/// </summary>
public static class VersionBumper {
    /// <summary>
    /// Computes the target version from the latest tag, project file versions, and diff results.
    /// The higher version between a hardcoded project version and a syntactic calculation always wins (§14).
    /// Major versions are never auto-bumped (§9).
    /// </summary>
    public static Version ComputeTargetVersion(
        Version? latestTag,
        Dictionary<string, Version> projectVersions,
        List<ProjectDiffResult> diffResults) {
        var baseVersion = latestTag ?? new Version(0, 0, 0);

        // Determine bump from syntactic analysis
        var hasBreaking = diffResults.Any(d => d.HasBreakingChanges);
        var hasAnyChanges = diffResults.Any(d => d.HasBreakingChanges || d.HasNonBreakingChanges);

        Version syntacticVersion;
        if (hasBreaking) {
            // Pre-1.0: Breaking → Minor bump. Post-1.0: still minor (major is manual-only)
            syntacticVersion = new Version(baseVersion.Major, baseVersion.Minor + 1, 0);
        } else if (hasAnyChanges) {
            // Non-Breaking → Patch bump
            syntacticVersion = new Version(baseVersion.Major, baseVersion.Minor, baseVersion.Build + 1);
        } else {
            // Deterministic fallback detected changes but no syntactic diff — patch bump
            syntacticVersion = new Version(baseVersion.Major, baseVersion.Minor, baseVersion.Build + 1);
        }

        // §14: Higher version between hardcoded project versions and syntactic calculation wins
        var maxProjectVersion = projectVersions.Values
            .DefaultIfEmpty(new Version(0, 0, 0))
            .Max()!;

        return syntacticVersion >= maxProjectVersion ? syntacticVersion : maxProjectVersion;
    }
}
