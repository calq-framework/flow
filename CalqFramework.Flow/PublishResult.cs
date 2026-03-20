using CalqFramework.Flow.Diff;

namespace CalqFramework.Flow;

/// <summary>
///     Result metadata returned by the Publish subcommand.
///     Serialized to JSON for machine-readable output.
/// </summary>
public class PublishResult {
    public string TargetVersion { get; set; } = "";
    public string PreviousVersion { get; set; } = "";
    public List<string> ChangedProjects { get; set; } = [];
    public List<string> PublishedPackages { get; set; } = [];
    public List<ProjectDiffResult> Diffs { get; set; } = [];
    public bool DryRun { get; set; }
}
