namespace CalqFramework.Flow.Versioning;

/// <summary>
///     Reads version from project files and resolves latest version tag from Git (§6).
/// </summary>
public static class VersionResolver {
    /// <summary>
    ///     Resolves the latest version tag from the remote using git ls-remote.
    /// </summary>
    public static Version? ResolveLatestTagVersion(string remote, string tagPrefix) {
        string tagPattern = $"{tagPrefix}[0-9]*.[0-9]*.[0-9]*";
        string output;
        try {
            output = CMD($"git ls-remote --tags --sort -version:refname {remote} \"{tagPattern}\"");
        } catch {
            return null;
        }

        if (string.IsNullOrWhiteSpace(output)) {
            return null;
        }

        // Take the first line (highest version due to sort)
        string? firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (firstLine == null) {
            return null;
        }

        // Extract tag name from refs/tags/{prefix}{version}
        string escapedPrefix = Regex.Escape(tagPrefix);
        Match match = Regex.Match(firstLine, $@"refs/tags/{escapedPrefix}(\d+\.\d+\.\d+)");
        if (!match.Success) {
            return null;
        }

        return Version.TryParse(match.Groups[1].Value, out Version? version) ? version : null;
    }

    /// <summary>
    ///     Reads the Version element from each project file.
    ///     Handles Version, VersionPrefix, and VersionSuffix.
    /// </summary>
    public static Dictionary<string, Version> ResolveProjectVersions(List<string> projects) {
        var result = new Dictionary<string, Version>();

        foreach (string project in projects) {
            Version? version = ReadVersionFromProject(project);
            if (version != null) {
                result[project] = version;
            }
        }

        return result;
    }

    /// <summary>
    ///     Reads Version from a single project file using XML parsing.
    /// </summary>
    public static Version? ReadVersionFromProject(string projectPath) {
        var doc = XDocument.Load(projectPath);
        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        // Try <Version> first
        XElement? versionElement = doc.Descendants(ns + "Version")
            .FirstOrDefault();
        if (versionElement != null) {
            string versionText = versionElement.Value.Trim();
            // Strip suffix if present (e.g., "1.0.0-beta")
            int dashIndex = versionText.IndexOf('-');
            if (dashIndex >= 0) {
                versionText = versionText[..dashIndex];
            }

            return Version.TryParse(versionText, out Version? v) ? v : null;
        }

        // Fall back to <VersionPrefix>
        XElement? prefixElement = doc.Descendants(ns + "VersionPrefix")
            .FirstOrDefault();
        if (prefixElement != null) {
            string prefixText = prefixElement.Value.Trim();
            return Version.TryParse(prefixText, out Version? v) ? v : null;
        }

        return null;
    }
}
