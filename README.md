[![NuGet Version](https://img.shields.io/nuget/v/CalqFramework.Flow.Cli?color=508cf0)](https://www.nuget.org/packages/CalqFramework.Flow.Cli)
[![NuGet Downloads](https://img.shields.io/nuget/dt/CalqFramework.Flow.Cli?color=508cf0)](https://www.nuget.org/packages/CalqFramework.Flow.Cli)
[![REUSE status](https://api.reuse.software/badge/github.com/calq-framework/flow)](https://api.reuse.software/info/github.com/calq-framework/flow)

# Calq Flow

Calq Flow is a deterministic versioning and CI/CD orchestration platform for .NET monorepos. It fully automates the release lifecycle—from project discovery and IL-level binary diffing to semantic versioning and NuGet publishing—all in a single command. It eliminates fragile DevOps scripts by treating your release pipeline as a natively compiled C# application, executing seamlessly as a CLI tool or a highly optimized GitHub Action.

## The Universal .NET Release Platform
Calq Flow transforms modular software delivery by collapsing the wall between application code and DevOps infrastructure. By shifting the source of truth from subjective commit messages to objective IL binary comparison, it delivers a zero-configuration, fully automated CI/CD pipeline that executes identically on your local workstation and in the cloud.

---

## Calq Flow vs. GitVersion

Traditional versioning relies on human-in-the-loop subjective data (commit messages, branch names). Calq Flow acts as an absolute engine of truth by analyzing the compiled binaries.

| Feature | Calq Flow | GitVersion |
| :--- | :--- | :--- |
| **Version Source** | Objective IL/metadata diff | Subjective Git history + branching strategy |
| **Breaking Change Detection** | Automatic (IL comparison) | Manual (commit message `+semver: breaking`) |
| **Monorepo Support** | Native project graph discovery | Not built-in |
| **Test Integration** | Auto-discovers and enforces test runs | Not included |
| **Build / Pack / Push** | Complete CI/CD pipeline orchestration | Not included (versioning only) |
| **Configuration** | Zero-config / CLI flags | `GitVersion.yml` |
| **GitHub Action Architecture** | Native Composite (Millisecond execution, zero Docker overhead) | Docker-based (Container initialization latency) |

---

## How It Works

Calq Flow orchestrates ten distinct stages of the release process that traditionally require a patchwork of disconnected YAML scripts:

```
discover → detect changes → build → resolve base DLLs → IL compare → version bump → pack → push → tag
```

1. **Discovery:** Recursively maps the monorepo, locating `*.*proj` files while excluding tests, examples, and nested projects.
2. **Impact Analysis:** Detects exactly which modules changed since the last version tag using `git diff`.
3. **Compilation & Testing:** Builds changed projects and automatically enforces associated test suites. Unchanged projects are also built to ensure lockstep packing.
4. **Base Resolution:** Acquires the previous version's DLL via NuGet download, or seamlessly falls back to a temporary "Shadow Copy" build of the previous Git tree.
5. **Syntactic Comparison:** Computes the objective truth of your codebase by comparing current vs. base assemblies at the IL/metadata level utilizing `MetadataLoadContext`.
6. **Versioning:** Algorithmically determines the semantic version: breaking → minor bump, non-breaking → patch bump (pre-1.0 convention). 
7. **Distribution & Persistence:** Packs all projects at the computed version, pushes to configured NuGet registries, and provisions a Git version tag.

---

## Usage

### As a GitHub Action

Because Calq Flow is architected as a C# Composite Action, it eliminates the substantial "Docker overhead" (container image pull and initialization latency) associated with conventional DevOps actions. It leverages the GitHub Runner's native .NET runtime to execute in milliseconds.

Publish workflows should utilize concurrency to prevent race conditions on version tags:

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

### As a CLI Tool (The "Infinite Loop" Workflow)

Calq Flow enables you to debug your CI pipeline locally. Because the orchestration logic is not confined to YAML, you can execute the exact same deterministic pipeline on your local workstation.

Install globally:

```bash
dotnet tool install --global CalqFramework.Flow.Cli
```

Run locally:

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

`publish` returns a `PublishResult` serialized to JSON on stdout. All build/test output is routed to stderr, maintaining a pristine stdout stream for machine consumption and integrations.

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

## Architecture & Rules

### Version Bumping Rules

Calq Flow algorithmically calculates version bumps based on IL changes, ensuring strict, policy-driven release management:

| Condition | Bump | Example |
| :--- | :--- | :--- |
| Breaking change (deleted/modified member) | Minor | `0.1.0` → `0.2.0` |
| Non-breaking change (added member / IL change) | Patch | `0.1.0` → `0.1.1` |
| Major version | Manual only | Edit `<Version>` in `.csproj` |

The higher version between the `.csproj` `<Version>` and the computed syntactic version always prevails, ensuring idempotency and robust manual override support.

### Project Discovery & Test Association

- Recursively maps `*.*proj` files.
- Excludes projects matching `*Test*`, `*Example*`, `*Sample*` (and identically named directories).
- Ignores nested projects.
- **Test Enforcement:** For each library project, Calq Flow automatically searches upward for `{ProjectName}Test*.*proj` files and strictly enforces test passes before authorizing a publish.

### Syntactic Versioning (The Objective IL Diff)

Assemblies are analyzed at the binary level using `System.Reflection.MetadataLoadContext`:

- **Breaking:** Deleted member or modified attributes.
- **Non-Breaking:** Added member or IL bytecode change.
- Compiler-generated attributes (`AsyncStateMachine`, `NullableContext`, `Nullable`, `CompilerGenerated`, `IteratorStateMachine`) are filtered out to prevent false positives.
- Falls back to byte-level comparison if no syntactic changes are found.

### The "Shadow Copy" Fallback

When the previous version's DLL cannot be retrieved from NuGet, Calq Flow relies on a highly resilient fallback mechanism. It automates standard developer file-operations (`cp` + `git checkout`) to provision a temporary workspace reflecting the repository's previous state, compiling the baseline DLLs in isolation. 

This deterministic approach guarantees the pipeline never fails due to an unreachable package registry. The primary working directory is never mutated. The shadow workspace is generated at most once per publish cycle and is automatically purged upon completion.

### Tagging & Branching

- Provisions a global tag `{prefix}{version}` (e.g., `v0.2.0`).
- Force-updates a rolling branch pointer (default `latest`) to the release commit. Disable by passing `--rolling-branch ""`.

---

## Building C# GitHub Actions

Calq Flow is internally architected as a C# GitHub Action. You can leverage its `action.yaml` and workflows to transform any strongly-typed .NET console application into a GitHub Action, eliminating the reliance on difficult-to-maintain bash and YAML configurations.

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

**3.** Adapt `action.yaml` to your requirements — remove steps you do not need, such as the self-install step.

**4. Setup Workflows**
Copy the templates from [`.github/workflows/`](https://github.com/calq-framework/flow/tree/main/.github/workflows). Replace `uses: ./` with `uses: your-org/your-tool@latest` and update the `subcommand` inputs to align with your tool's CLI.

## License
Calq Flow is dual-licensed under GNU AGPLv3 and the Calq Commercial License.
