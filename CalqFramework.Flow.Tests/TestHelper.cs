namespace CalqFramework.Flow.Tests;

/// <summary>
///     Shared utilities for setting up local git repos and .NET projects for testing.
///     Uses CalqFramework.Cmd for all git/dotnet operations.
/// </summary>
public static class TestHelper {
    /// <summary>
    ///     Creates a bare git repository to act as a local "remote".
    /// </summary>
    public static string CreateBareRepo() {
        string path = Path.Combine(Path.GetTempPath(), $"flow-test-bare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        string prev = PWD;
        CD(path);
        CMD("git init --bare");
        CD(prev);
        return path;
    }

    /// <summary>
    ///     Creates a working git repo linked to the given bare repo as "origin".
    /// </summary>
    public static string CreateWorkingRepo(string bareRepoPath) {
        string path = Path.Combine(Path.GetTempPath(), $"flow-test-work-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        string prev = PWD;
        CD(path);
        CMD("git init -b main");
        CMD("git config user.email \"test@test.com\"");
        CMD("git config user.name \"Test\"");
        CMD($"git remote add origin \"{bareRepoPath}\"");
        CD(prev);
        return path;
    }

    /// <summary>
    ///     Creates a minimal .csproj file with a Version element.
    /// </summary>
    public static string CreateProject(string parentDir, string projectName, string version = "1.0.0") {
        string projectDir = Path.Combine(parentDir, projectName);
        Directory.CreateDirectory(projectDir);
        string csprojPath = Path.Combine(projectDir, $"{projectName}.csproj");
        File.WriteAllText(
            csprojPath,
            $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Version>{version}</Version>
  </PropertyGroup>
</Project>");
        return csprojPath;
    }

    /// <summary>
    ///     Creates a minimal .cs source file in the given directory.
    /// </summary>
    public static string CreateSourceFile(string projectDir, string className, string content = "") {
        if (string.IsNullOrEmpty(content)) {
            content = $@"namespace TestLib;
public class {className} {{
    public string Hello() => ""world"";
}}";
        }

        string filePath = Path.Combine(projectDir, $"{className}.cs");
        File.WriteAllText(filePath, content);
        return filePath;
    }

    /// <summary>
    ///     Commits all files in the working repo and pushes to origin.
    /// </summary>
    public static void CommitAndPush(string workingDir, string message = "commit", string branch = "main") {
        string prev = PWD;
        CD(workingDir);
        CMD("git add -A");
        CMD($"git commit -m \"{message}\" --allow-empty");
        try {
            CMD($"git push origin {branch}");
        } catch {
            // First push — set upstream
            CMD($"git push -u origin {branch}");
        }

        CD(prev);
    }

    /// <summary>
    ///     Creates a version tag and pushes it to origin.
    /// </summary>
    public static void TagAndPush(string workingDir, string tag, string remote = "origin") {
        string prev = PWD;
        CD(workingDir);
        CMD($"git tag {tag}");
        CMD($"git push {remote} {tag}");
        CD(prev);
    }

    /// <summary>
    ///     Suppresses RUN logging for cleaner test output.
    /// </summary>
    public static void SuppressLogging() {
        LocalTerminal.TerminalLogger = new NullTerminalLogger();
        LocalTerminal.Out = Stream.Null;
    }

    /// <summary>
    ///     Recursively deletes a directory, ignoring errors.
    /// </summary>
    public static void CleanupDir(string path) {
        try {
            if (Directory.Exists(path)) {
                // Reset readonly attributes on .git files
                foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories)) {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(path, true);
            }
        } catch {
            // Best effort cleanup
        }
    }
}
