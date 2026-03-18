using CalqFramework.Flow.Diff;
using CalqFramework.Flow.Discovery;
using CalqFramework.Flow.Git;
using CalqFramework.Flow.Pipeline;
using CalqFramework.Flow.Versioning;
using static CalqFramework.Cmd.Terminal;

namespace CalqFramework.Flow;

/// <summary>
/// Deterministic versioning and publishing for .NET monorepos.
/// </summary>
public class FlowManager {
    // ── Global Options ──

    /// <summary>
    /// NuGet source names to push packages to.
    /// If "nuget.org" is specified, the NUGET_API_KEY environment variable is used automatically.
    /// </summary>
    public List<string> Sources { get; set; } = new();

    /// <summary>
    /// Git remote name for tag resolution and fetch operations.
    /// </summary>
    public string Remote { get; set; } = "origin";

    /// <summary>
    /// Prefix for version tags.
    /// </summary>
    public string TagPrefix { get; set; } = "v";

    // ── Subcommands ──

    /// <summary>
    /// Publishes changed projects to configured NuGet sources.
    /// Resolves target versions, computes syntactic diffs, attempts mirroring,
    /// falls back to source build, and synchronizes Git tags.
    /// Always returns the syntactic diff metadata.
    /// </summary>
    /// <param name="dryRun">Log actions without modifying the filesystem, Git state, or NuGet registries.</param>
    /// <param name="ignoreAccessModifiers">Include internal member changes (for InternalsVisibleTo).</param>
    /// <param name="sign">Certificate fingerprint or path for signing .nupkg files before push.</param>
    /// <param name="rollingBranch">Branch pointer to force-update on release. Empty string disables.</param>
    public PublishResult Publish(
        bool dryRun = false,
        bool ignoreAccessModifiers = false,
        string sign = "",
        string rollingBranch = "latest"
    ) {
        if (Sources.Count == 0) {
            Sources = new() { "main" };
        }

        // Redirect RUN output to stderr so stdout stays clean for JSON
        LocalTerminal.Out = Console.OpenStandardError();

        var workingDirectory = Environment.CurrentDirectory;
        var repoRoot = GitOperations.GetRepositoryRoot(workingDirectory);

        // §4: Project Discovery
        var projects = ProjectDiscovery.DiscoverProjects(workingDirectory);

        // §5: Test Project Association
        var testAssociations = TestProjectAssociation.FindTestProjects(projects, repoRoot);

        // §6: Version Evaluation
        var latestTag = VersionResolver.ResolveLatestTagVersion(Remote, TagPrefix);
        var projectVersions = VersionResolver.ResolveProjectVersions(projects);

        // §7: Changed Project Detection
        var changedProjects = ChangeDetection.DetectChangedProjects(projects, Remote, TagPrefix);

        // §8-9: Syntactic Versioning + Version Bumping
        var diffResults = new List<ProjectDiffResult>();
        Version targetVersion = latestTag ?? new Version(0, 0, 0);

        if (changedProjects.Count > 0) {
            var shadowCopyPath = ShadowCopy.Create(workingDirectory, repoRoot, Remote, TagPrefix);
            try {
                foreach (var project in changedProjects) {
                    var diff = SyntacticVersioning.Compare(
                        project, shadowCopyPath, workingDirectory, ignoreAccessModifiers);
                    diffResults.Add(diff);
                }
            } finally {
                ShadowCopy.Cleanup(shadowCopyPath);
            }

            targetVersion = VersionBumper.ComputeTargetVersion(
                latestTag, projectVersions, diffResults);
        }

        // §10: Publish Pipeline
        var publishedPackages = new List<string>();
        if (changedProjects.Count > 0) {
            publishedPackages = PublishPipeline.Execute(
                projects, changedProjects, testAssociations,
                targetVersion, Sources, sign, dryRun, Remote, TagPrefix);
        }

        // §12: Tagging & Branching
        if (!dryRun && changedProjects.Count > 0) {
            TaggingStrategy.CreateTag(TagPrefix, targetVersion, Remote);
            if (!string.IsNullOrEmpty(rollingBranch)) {
                TaggingStrategy.UpdateRollingBranch(rollingBranch, Remote);
            }
        }

        return new PublishResult {
            TargetVersion = targetVersion.ToString(3),
            PreviousVersion = latestTag?.ToString(3) ?? "",
            ChangedProjects = changedProjects.Select(p => Path.GetFileNameWithoutExtension(p)).ToList(),
            PublishedPackages = publishedPackages,
            Diffs = diffResults,
            DryRun = dryRun
        };
    }
}
