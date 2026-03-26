using CalqFramework.Flow.Diff;
using CalqFramework.Flow.Discovery;
using CalqFramework.Flow.Git;
using CalqFramework.Flow.Pipeline;
using CalqFramework.Flow.Versioning;

namespace CalqFramework.Flow;

/// <summary>
///     Deterministic versioning and publishing for .NET monorepos.
/// </summary>
public class FlowManager {
    // ── Global Options ──

    /// <summary>
    ///     NuGet source names to push packages to.
    ///     If "nuget.org" is specified, the NUGET_API_KEY environment variable is used automatically.
    /// </summary>
    public List<string> Sources { get; set; } = [];

    /// <summary>
    ///     Git remote name for tag resolution and fetch operations.
    /// </summary>
    public string Remote { get; set; } = "origin";

    /// <summary>
    ///     Prefix for version tags.
    /// </summary>
    public string TagPrefix { get; set; } = "v";

    // ── Subcommands ──

    /// <summary>
    ///     Publishes changed projects to configured NuGet sources.
    ///     Pipeline: discover → detect changes → build current → resolve base DLLs →
    ///     compare → compute version → pack → push → tag.
    /// </summary>
    /// <param name="dryRun">Log actions without modifying the filesystem, Git state, or NuGet registries.</param>
    /// <param name="ignoreAccessModifiers">Include internal member changes (for InternalsVisibleTo).</param>
    /// <param name="sign">Certificate fingerprint or path for signing .nupkg files before push.</param>
    /// <param name="rollingBranch">Branch pointer to force-update on release. Empty string disables.</param>
    /// <param name="apiKey">API key for authenticated NuGet push operations.</param>
    public PublishResult Publish(bool dryRun = false, bool ignoreAccessModifiers = false, string sign = "", string rollingBranch = "latest", string apiKey = "") {
        if (Sources.Count == 0) {
            Sources = [
                "main"
            ];
        }

        // Redirect RUN output to stderr so stdout stays clean for JSON
        LocalTerminal.Out = Console.OpenStandardError();

        string workingDirectory = PWD;
        string repoRoot = GitOperations.GetRepositoryRoot(workingDirectory);

        // §4: Project Discovery
        List<string> projects = ProjectDiscovery.DiscoverProjects(workingDirectory);

        // §5: Test Project Association
        Dictionary<string, string> testAssociations = TestProjectAssociation.FindTestProjects(projects, repoRoot);

        // §6: Version Evaluation
        Version? latestTag = VersionResolver.ResolveLatestTagVersion(Remote, TagPrefix);
        Dictionary<string, Version> projectVersions = VersionResolver.ResolveProjectVersions(projects);

        // §7: Changed Project Detection
        List<string> changedProjects = ChangeDetection.DetectChangedProjects(projects, Remote, TagPrefix);

        if (changedProjects.Count == 0) {
            // No source changes — but the tagged version may not be published to all
            // requested sources yet (e.g. first publish goes to GitHub Packages, second
            // workflow republishes to nuget.org). Download existing .nupkg and re-push.
            if (latestTag != null) {
                var downloadedNupkgs = new List<string>();
                foreach (string project in projects) {
                    string packageId = Path.GetFileNameWithoutExtension(project);
                    string? nupkg = BuildPipeline.TryDownloadFromNuGet(packageId, latestTag, "*.nupkg");
                    if (nupkg != null) {
                        downloadedNupkgs.Add(nupkg);
                    }
                }

                if (downloadedNupkgs.Count > 0) {
                    var republishedPackages = new List<string>();
                    if (dryRun) {
                        foreach (string project in projects) {
                            string name = Path.GetFileNameWithoutExtension(project);
                            Console.Error.WriteLine($"[dry-run] Would republish {name} {latestTag.ToString(3)}");
                            republishedPackages.Add(name);
                        }
                    } else {
                        republishedPackages = PublishPipeline.Execute(downloadedNupkgs, Sources, sign, apiKey, false);
                    }

                    if (!dryRun && !string.IsNullOrEmpty(rollingBranch)) {
                        TaggingStrategy.UpdateRollingBranch(rollingBranch, Remote);
                    }

                    return new PublishResult {
                        TargetVersion = latestTag.ToString(3),
                        PreviousVersion = latestTag.ToString(3),
                        ChangedProjects = [.. projects.Select(p => Path.GetFileNameWithoutExtension(p))],
                        PublishedPackages = republishedPackages,
                        Diffs = [],
                        DryRun = dryRun
                    };
                }
            }

            // No tag or no packages found — nothing to do
            if (!dryRun && !string.IsNullOrEmpty(rollingBranch)) {
                TaggingStrategy.UpdateRollingBranch(rollingBranch, Remote);
            }

            return new PublishResult {
                TargetVersion = (latestTag ?? new Version(0, 0, 0)).ToString(3),
                PreviousVersion = latestTag?.ToString(3) ?? "",
                DryRun = dryRun
            };
        }

        // ── Phase 1: Build current projects ──
        // Changed projects are built and tested. Remaining projects are built for lockstep packing.
        foreach (string project in changedProjects) {
            BuildPipeline.BuildCurrent(project, testAssociations);
        }
        foreach (string project in projects) {
            if (!changedProjects.Contains(project)) {
                BuildPipeline.BuildCurrent(project, testAssociations);
            }
        }

        // ── Phase 2: Resolve base DLLs and compare ──
        // Try NuGet download first, fall back to building in shadow copy.
        string? shadowCopyPath = null;
        var diffResults = new List<ProjectDiffResult>();

        try {
            foreach (string project in changedProjects) {
                string projectName = Path.GetFileNameWithoutExtension(project);
                string projectDir = Path.GetDirectoryName(project)!;

                string? currentDll = SyntacticVersioning.FindAssembly(projectDir, projectName);

                // Resolve base DLL: NuGet first, then shadow copy
                string? baseDll = null;
                if (latestTag != null) {
                    baseDll = BuildPipeline.ResolveBaseDll(project, projectName, latestTag, Sources, null);

                    // NuGet failed — create shadow copy (once) and build there
                    if (baseDll == null) {
                        shadowCopyPath ??= ShadowCopy.Create(workingDirectory, repoRoot, Remote, TagPrefix);
                        baseDll = BuildPipeline.ResolveBaseDll(project, projectName, latestTag, Sources, shadowCopyPath);
                    }
                }

                ProjectDiffResult diff = SyntacticVersioning.Compare(projectName, currentDll, baseDll, ignoreAccessModifiers);
                diffResults.Add(diff);
            }
        } finally {
            if (shadowCopyPath != null) {
                ShadowCopy.Cleanup(shadowCopyPath);
            }
        }

        // §9: Version Bumping
        Version targetVersion = VersionBumper.ComputeTargetVersion(latestTag, projectVersions, diffResults);

        // ── Phase 3: Pack and push (lockstep — all projects at the same version) ──
        var nupkgPaths = new List<string>();
        foreach (string project in projects) {
            string? nupkg = BuildPipeline.Pack(project, targetVersion);
            if (nupkg != null) {
                nupkgPaths.Add(nupkg);
            }
        }

        var publishedPackages = new List<string>();
        if (dryRun) {
            foreach (string project in projects) {
                string name = Path.GetFileNameWithoutExtension(project);
                Console.Error.WriteLine($"[dry-run] Would publish {name} {targetVersion.ToString(3)}");
                publishedPackages.Add(name);
            }
        } else {
            publishedPackages = PublishPipeline.Execute(nupkgPaths, Sources, sign, apiKey, false);
        }

        // §12: Tagging & Branching
        if (!dryRun) {
            TaggingStrategy.CreateTag(TagPrefix, targetVersion, Remote);
            if (!string.IsNullOrEmpty(rollingBranch)) {
                TaggingStrategy.UpdateRollingBranch(rollingBranch, Remote);
            }
        }

        return new PublishResult {
            TargetVersion = targetVersion.ToString(3),
            PreviousVersion = latestTag?.ToString(3) ?? "",
            ChangedProjects = [.. projects.Select(p => Path.GetFileNameWithoutExtension(p))],
            PublishedPackages = publishedPackages,
            Diffs = diffResults,
            DryRun = dryRun
        };
    }
}
