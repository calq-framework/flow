using System.Text.RegularExpressions;
using System.Xml.Linq;
using static CalqFramework.Cmd.Terminal;

namespace CalqFramework.Flow.Versioning;

/// <summary>
/// Reads version from project files and resolves latest version tag from Git (§6).
/// </summary>
public static class VersionResolver {
    /// <summary>
    /// Resolves the latest version tag from the remote using git ls-remote.
    /// </summary>
    public static Version? ResolveLatestTagVersion(string remote, string tagPrefix) {
        var tagPattern = $"{tagPrefix}[0-9]*.[0-9]*.[0-9]*";
        string output;
        try {
            output = CMD($"git ls-remote --tags --sort -version:refname {remote} \"{tagPattern}\"");
        } catch {
            return null;
        }

        if (string.IsNullOrWhiteSpace(output)) return null;

        // Take the first line (highest version due to sort)
        var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstLine == null) return null;

        // Extract tag name from refs/tags/{prefix}{version}
        var escapedPrefix = Regex.Escape(tagPrefix);
        var match = Regex.Match(firstLine, $@"refs/tags/{escapedPrefix}(\d+\.\d+\.\d+)");
        if (!match.Success) return null;

        return Version.TryParse(match.Groups[1].Value, out var version) ? version : null;
    }

    /// <summary>
    /// Reads the Version element from each project file.
    /// Handles Version, VersionPrefix, and VersionSuffix.
    /// </summary>
    public static Dictionary<string, Version> ResolveProjectVersions(List<string> projects) {
        var result = new Dictionary<string, Version>();

        foreach (var project in projects) {
            var version = ReadVersionFromProject(project);
            if (version != null) {
                result[project] = version;
            }
        }

        return result;
    }

    /// <summary>
    /// Reads Version from a single project file using XML parsing.
    /// </summary>
    public static Version? ReadVersionFromProject(string projectPath) {
        var doc = XDocument.Load(projectPath);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        // Try <Version> first
        var versionElement = doc.Descendants(ns + "Version").FirstOrDefault();
        if (versionElement != null) {
            var versionText = versionElement.Value.Trim();
            // Strip suffix if present (e.g., "1.0.0-beta")
            var dashIndex = versionText.IndexOf('-');
            if (dashIndex >= 0) versionText = versionText[..dashIndex];
            return Version.TryParse(versionText, out var v) ? v : null;
        }

        // Fall back to <VersionPrefix>
        var prefixElement = doc.Descendants(ns + "VersionPrefix").FirstOrDefault();
        if (prefixElement != null) {
            var prefixText = prefixElement.Value.Trim();
            return Version.TryParse(prefixText, out var v) ? v : null;
        }

        return null;
    }
}
