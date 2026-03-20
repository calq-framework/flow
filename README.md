[![NuGet Version](https://img.shields.io/nuget/v/CalqFramework.Flow.Cli?color=508cf0)](https://www.nuget.org/packages/CalqFramework.Flow.Cli)
[![NuGet Downloads](https://img.shields.io/nuget/dt/CalqFramework.Flow.Cli?color=508cf0)](https://www.nuget.org/packages/CalqFramework.Flow.Cli)
[![REUSE status](https://api.reuse.software/badge/github.com/calq-framework/flow)](https://api.reuse.software/info/github.com/calq-framework/flow)

# Calq Flow
Calq Flow is a deterministic versioning and publishing tool for .NET monorepos. It automatically discovers projects, compares assemblies at the IL level, computes semantic version bumps, and publishes NuGet packages — all in a single command.  
Calq Flow runs as a CLI tool, a GitHub Action, or both.

## How It Works

```
discover → detect changes → build → resolve base DLLs → IL compare → version bump → pack → push → tag
```

1. Recursively discovers `*.*proj` files, excluding tests, examples, and nested projects.
2. Detects which projects changed since the last version tag using `git diff`.
3. Builds changed projects (and runs associated tests). Unchanged projects are also built for lockstep packing.
4. Resolves the previous version's DLL via NuGet download or shadow-copy build.
5. Compares current vs. base assemblies at the IL/metadata level using `MetadataLoadContext`.
6. Computes the target version: breaking → minor bump, non-breaking → patch bump (pre-1.0 convention). Major bumps are manual only.
7. Packs all projects at the computed version, pushes to configured NuGet sources, and creates a Git version tag.

## Calq Flow vs. GitVersion

| Feature | Calq Flow | GitVersion |
| :--- | :--- | :--- |
| **Version Source** | IL/metadata diff | Git history + branching strategy |
| **Breaking Change Detection** | Automatic (IL comparison) | Manual (commit message `+semver: breaking`) |
| **Monorepo Support** | Built-in project discovery | Not built-in |
| **Test Integration** | Auto-discovers and runs test projects | Not included |
| **Build / Pack / Push** | Built-in pipeline | Not included (versioning only) |
| **Configuration** | CLI flags | `GitVersion.yml` |
| **GitHub Action** | Composite (no Docker) | Docker-based |

## Usage

### As a GitHub Action

Publish workflows should use concurrency to prevent race conditions on version tags:

```yaml
name: Publish

on:
  workflow_dispatch:

concurrency:
  group: publish
  cancel-in-progress: false

jobs:
  publish:
    runs-on: ubuntu-latest
    permissions:
      contents: write
      packages: write
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Calq Flow (publish)
        uses: calq-framework/flow@latest
        with:
          subcommand: 'publish --api-key ${{ github.token }}'
        env: ${{ secrets }}
```

Pin to a specific version:

```yaml
- uses: calq-framework/flow@v0.1.0
  with:
    subcommand: 'publish'
```

Dry-run on pull requests:

```yaml
- uses: calq-framework/flow@latest
  with:
    subcommand: 'publish --dry-run'
```

Publish to nuget.org with an API key:

```yaml
- uses: calq-framework/flow@latest
  with:
    subcommand: 'publish --sources nuget.org --api-key ${{ secrets.NUGET_API_KEY }}'
```

### Action Inputs

| Input | Required | Default | Description |
| :--- | :--- | :--- | :--- |
| `subcommand` | Yes | — | The flow subcommand to execute (e.g., `publish`, `publish --dry-run`) |
| `nuget-config-repo` | No | `.nuget` | Repository to pull `NuGet.Config` from (under the same GitHub owner) |
| `cache` | No | `true` | Enable caching for the tool binary and NuGet packages |

### As a CLI Tool

Install globally:

```bash
dotnet tool install --global CalqFramework.Flow.Cli
```

Run:

```bash
calq-flow publish
calq-flow publish --dry-run
calq-flow publish --sources nuget.org --api-key <key>
```

### Global Options

| Option | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `--sources` / `-s` | `List<string>` | `["main"]` | NuGet source names to push packages to |
| `--remote` / `-r` | `string` | `origin` | Git remote name for tag resolution and fetch operations |
| `--tag-prefix` / `-t` | `string` | `v` | Prefix for version tags |

### Publish Parameters

| Parameter | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `--dry-run` | `bool` | `false` | Log actions without modifying filesystem, Git, or NuGet |
| `--ignore-access-modifiers` | `bool` | `false` | Include internal member changes (for `InternalsVisibleTo`) |
| `--sign` | `string` | `""` | Certificate fingerprint for signing `.nupkg` files before push |
| `--rolling-branch` | `string` | `latest` | Branch pointer to force-update on release (empty string disables) |
| `--api-key` | `string` | `""` | API key for authenticated NuGet push operations |

### JSON Output

`publish` returns a `PublishResult` serialized to JSON on stdout. All build/test output goes to stderr, keeping stdout clean for machine consumption.

```json
{
  "TargetVersion": "0.2.0",
  "PreviousVersion": "0.1.0",
  "ChangedProjects": ["MyLibrary"],
  "PublishedPackages": ["MyLibrary"],
  "Diffs": [
    {
      "ProjectName": "MyLibrary",
      "Changes": [
        {
          "MemberIdentity": "MyNamespace.MyClass.NewMethod(System.String)",
          "Kind": "Added"
        }
      ],
      "HasBreakingChanges": false,
      "HasNonBreakingChanges": true,
      "ByteLevelFallback": false
    }
  ],
  "DryRun": false
}
```

---

### Version Bumping Rules

| Condition | Bump | Example |
| :--- | :--- | :--- |
| Breaking change (deleted/modified member) | Minor | `0.1.0` → `0.2.0` |
| Non-breaking change (added member / IL change) | Patch | `0.1.0` → `0.1.1` |
| Major version | Manual only | Edit `<Version>` in `.csproj` |

The higher version between the `.csproj` `<Version>` and the computed syntactic version always wins, ensuring idempotency and manual override support.

### Project Discovery

- Recursively scans for `*.*proj` files.
- Excludes projects matching `*Test*`, `*Example*`, `*Sample*` (and identically named directories).
- Ignores nested projects (a project inside another project's directory tree).

### Test Project Association

For each discovered library project, Calq Flow searches upward from the project directory for files matching `{ProjectName}Test*.*proj`, bounded by the Git repository root.

### Syntactic Versioning (IL Comparison)

Assemblies are compared at the IL/metadata level using `System.Reflection.MetadataLoadContext`:

- **Breaking:** Deleted member or modified attributes.
- **Non-Breaking:** Added member or IL bytecode change.
- Compiler-generated attributes (`AsyncStateMachine`, `NullableContext`, `Nullable`, `CompilerGenerated`, `IteratorStateMachine`) are filtered out to prevent false positives.
- Falls back to byte-level comparison if no syntactic changes are found.

### Shadow Copy

When the previous version's DLL can't be downloaded from NuGet, Calq Flow creates a temporary copy of the repository, checks out the base commit, and builds the old version there. The original working directory is never modified. The shadow copy is created at most once per publish run and cleaned up afterward.

### Tagging & Branching

- Creates a global tag `{prefix}{version}` (e.g., `v0.2.0`).
- Force-updates a rolling branch pointer (default `latest`) to the release commit. Disable by passing `--rolling-branch ""`.

---

## Building C# GitHub Actions

Calq Flow is itself a C# GitHub Action. You can reuse its `action.yaml` and workflows to turn any .NET console app into a GitHub Action.

### Steps

**1.** Add these properties to your `.csproj`:

```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>your-tool</ToolCommandName>
<PackageId>YourTool.Cli</PackageId>
```

**2.** Copy [`action.yaml`](https://github.com/calq-framework/flow/blob/main/action.yaml) to your repository root. Update the four variables in the first step:

```bash
TOOL_NAME="Your Tool"
TOOL_REPO_URL="https://github.com/your-org/your-tool.git"
TOOL_PACKAGE="YourTool.Cli"
TOOL_CMD="your-tool"
```

**3.** Adapt `action.yaml` to your needs — remove steps you don't need, such as the self-test install.

**4.** Copy the workflows from [`.github/workflows/`](https://github.com/calq-framework/flow/tree/main/.github/workflows). Replace `uses: ./` with `uses: your-org/your-tool@latest` (or `@v1.0.0`), and adjust the `subcommand` values. The `./` path is only used by Calq Flow to test its own action from source.

## License
Calq Flow is dual-licensed under GNU AGPLv3 and the Calq Commercial License.
