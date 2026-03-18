using CalqFramework.Flow.Diff;

namespace CalqFramework.Flow;

/// <summary>
/// Result metadata returned by the Publish subcommand.
/// Serialized to JSON for machine-readable output.
/// </summary>
public class PublishResult {
    public string TargetVersion { get; set; } = "";
    public string PreviousVersion { get; set; } = "";
    public List<string> ChangedProjects { get; set; } = new();
    public List<string> PublishedPackages { get; set; } = new();
    public List<ProjectDiffResult> Diffs { get; set; } = new();
    public bool DryRun { get; set; }
}
