# NetForward

**A migration toolkit for legacy .NET Framework applications.** NetForward analyzes your old .NET solutions, scores their migration readiness, and automates the parts of the .NET Framework → modern .NET (8/9) migration that can be done deterministically — so engineers spend their time on the parts that actually need human judgment.

> **Status: Phase 1 (Analyzer + CSProj Modernizer) — released**

---

## Table of contents

1. [Why NetForward](#why-netforward)
2. [What it does today](#what-it-does-today)
3. [How it works](#how-it-works)
4. [Quick start](#quick-start)
5. [Using the CLI](#using-the-cli)
6. [Using the Blazor dashboard](#using-the-blazor-dashboard)
7. [Reading the readiness report](#reading-the-readiness-report)
8. [Modernizing a `.csproj`](#modernizing-a-csproj)
9. [Recommended migration workflow](#recommended-migration-workflow)
10. [Issue ID reference](#issue-id-reference)
11. [Extending the compatibility catalog](#extending-the-compatibility-catalog)
12. [Solution structure](#solution-structure)
13. [Build, test, and install](#build-test-and-install)
14. [Troubleshooting](#troubleshooting)
15. [FAQ](#faq)
16. [Roadmap](#roadmap)

---

## Why NetForward

Most teams migrating from .NET Framework to modern .NET hit the same wall: the official `dotnet upgrade-assistant` handles the easy 30%, then leaves you with a half-converted solution and no clear picture of what's left. Legacy `.csproj` files, `packages.config`, `Web.config`, MVC 5 idioms, EF6, WCF, OWIN — all of it requires careful, repetitive, error-prone manual work.

NetForward's goal is to **reduce manual migration effort by at least 75% across the full lifecycle** — analysis, planning, scaffolding, code conversion, and verification — by automating everything that can be automated and producing actionable, evidence-based guidance for everything that can't.

---

## What it does today

Phase 1 (current release) ships:

- **Solution analyzer** that scans `.sln` and `.csproj` files, classifies each project (MVC, Web API, WebForms, WCF, WinForms, WPF, etc.), inventories NuGet packages and dependencies, and detects legacy patterns (`Web.config`, `Global.asax`, `packages.config`, etc.).
- **Compatibility catalog** — a contributable, YAML-backed database of legacy → modern API and package mappings (27+ packages and 22+ APIs at launch, covering ASP.NET MVC, Web API, EF6, OWIN, WCF, Identity, log4net, NLog, Autofac, Newtonsoft.Json, and more).
- **Readiness scoring** — a transparent 0–100 score per project and per solution, with severity-weighted issues so you can see exactly what's costing points.
- **Effort estimation** — per-issue and rolled-up hour estimates so planning is grounded in evidence rather than gut feel.
- **Four report formats** out of the box:
  - **JSON** — machine-readable, ideal for CI/CD pipelines and custom tooling
  - **Markdown** — perfect for committing alongside source or pasting into PR descriptions
  - **HTML** — self-contained dashboard you can email or upload anywhere
  - **Word (.docx)** — stakeholder-ready document for management review
- **`.csproj` modernizer** — converts a legacy verbose `.csproj` to modern SDK-style format, merging `packages.config` into `<PackageReference>` items. Non-destructive: writes a `.modernized.csproj` next to the original; nothing is overwritten.
- **`netforward` CLI tool** — runnable as a `dotnet` global tool, scriptable in CI/CD pipelines.
- **Blazor Server dashboard** — interactive web UI for running analyses and browsing results.

What it does **not** do yet (planned for later phases): rewrite C# code, generate tests, scaffold SPA frontends, advise on desktop-to-web conversion. See the [roadmap](#roadmap).

---

## How it works

NetForward is a layered pipeline. Each layer has a single responsibility and depends only on the layers beneath it:

```
┌─────────────────────────────────────────────────────────┐
│  CLI  /  Blazor Dashboard                               │  Entry points
├─────────────────────────────────────────────────────────┤
│  Reporting (JSON, Markdown, HTML, Word)                 │  Output formats
├─────────────────────────────────────────────────────────┤
│  Modernizer (csproj converter)                          │  Mutations
├─────────────────────────────────────────────────────────┤
│  Analyzer  ──────────►  Compatibility Catalog           │  Detection
├─────────────────────────────────────────────────────────┤
│  Core (domain models + abstractions)                    │  Contracts
└─────────────────────────────────────────────────────────┘
```

When you run `netforward analyze SomeApp.sln`, the flow is:

1. **Solution parser** reads the `.sln` file and lists every `.csproj` it references.
2. For each project, the **csproj parser** extracts the target framework, SDK type, package references, project references, and assembly references — handling both legacy verbose csproj and modern SDK-style.
3. The **packages.config parser** picks up legacy NuGet dependencies if present.
4. The **project classifier** combines those signals to label the project (`AspNetMvc`, `WebForms`, `WinForms`, etc.).
5. The **issue detector** raises stable-IDed findings (`NF001`, `NF200`, …) for every legacy pattern it sees, looking each one up in the **compatibility catalog** for context and recommended replacements.
6. The **readiness scorer** turns issues into a 0–100 score using transparent, documented weights.
7. The **report formatters** render the same `SolutionAnalysisResult` into JSON, Markdown, HTML, and Word.

The whole thing is non-destructive — your source repo is never modified. Every output goes to a separate directory you control.

---

## Quick start

### Prerequisites

- **.NET 8 SDK** installed: https://dotnet.microsoft.com/download/dotnet/8.0
- A legacy `.NET` solution to analyze (or use the bundled sample)

### Five-minute test drive

```bash
# 1. Extract NetForward.zip to a folder of your choice
# 2. Open a terminal in that folder

# 3. Build everything
dotnet build

# 4. Run the tests (verifies the install)
dotnet test

# 5. Analyze the bundled sample legacy MVC app
dotnet run --project src/NetForward.Cli -- analyze tests/TestAssets/LegacyMvcApp.sln

# 6. Open the HTML report in your browser
#    (path is printed in the console output)
```

You should see something like this on the console:

```
Readiness score : 55/100
Projects        : 1
Total issues    : 13
Blockers        : 0
Estimated effort: 19.8h
```

And four report files in `./netforward-report/`:

```
LegacyMvcApp.json
LegacyMvcApp.md
LegacyMvcApp.html
LegacyMvcApp.docx
```

---

## Using the CLI

### `netforward analyze`

Analyze a solution and produce reports.

```bash
dotnet run --project src/NetForward.Cli -- analyze <SOLUTION.sln> [options]
```

| Option | Default | Description |
|---|---|---|
| `<SOLUTION.sln>` | required | Path to the `.sln` file to analyze. |
| `-o, --output <DIR>` | `./netforward-report` | Directory to write reports into. Created if missing. |
| `-f, --format <fmt>` | `json,markdown,html,word` | One or more formats. Repeat the option or comma-separate. |
| `--verbose` | off | Verbose logging. Useful for diagnosing parser issues. |

**Examples:**

```bash
# Analyze with all four reports (default)
dotnet run --project src/NetForward.Cli -- analyze C:\src\Legacy.sln

# Just Markdown and JSON, custom output directory
dotnet run --project src/NetForward.Cli -- analyze C:\src\Legacy.sln \
  --format markdown,json --output C:\reports

# Verbose for debugging a misclassification
dotnet run --project src/NetForward.Cli -- analyze C:\src\Legacy.sln --verbose
```

**Exit codes:**

| Code | Meaning |
|---|---|
| `0` | Analysis succeeded, no blockers found. |
| `1` | Solution file not found. |
| `2` | Analyzer threw an exception. |
| `3` | No valid report formats were requested. |
| `10` | Analysis succeeded, but blockers were found. (Useful for failing CI builds on regression.) |

### `netforward modernize-csproj`

Convert a single legacy `.csproj` to modern SDK-style format.

```bash
dotnet run --project src/NetForward.Cli -- modernize-csproj <PROJECT.csproj> [options]
```

| Option | Default | Description |
|---|---|---|
| `<PROJECT.csproj>` | required | Path to the legacy `.csproj` to convert. |
| `--out <PATH>` | `<project>.modernized.csproj` | Where to write the new file. |
| `--verbose` | off | Verbose logging. |

**Example:**

```bash
dotnet run --project src/NetForward.Cli -- modernize-csproj C:\src\Old.csproj
# writes C:\src\Old.modernized.csproj
```

The original file is **never modified**. Review the modernized version, run a build, and replace the original only when you're satisfied.

---

## Using the Blazor dashboard

```bash
dotnet run --project src/NetForward.Dashboard
```

Then open the URL printed in the console (typically `http://localhost:5000` or similar). Paste in any solution path on your machine and click **Analyze** — you'll get a live interactive view with all the same data as the HTML report, but rendered in the browser.

The dashboard is useful when you want to quickly explore multiple solutions without generating files for each one.

---

## Reading the readiness report

The HTML report is the most scannable format and the recommended starting point. From the top:

### 1. Header strip

Five summary tiles:

- **Readiness score** (0–100) — the headline number
- **Projects** — total project count
- **Issues** — total findings across all projects
- **Blockers** — issues with severity `Blocker` (require architectural redesign)
- **Estimated effort** — total hours of manual work, rolled up from per-issue estimates

### 2. Projects table

One row per project, with type, target framework, SDK status, issue count, effort estimate, and recommended migration target.

Click a project name to jump to its detail section.

### 3. Per-project sections

Each project shows:

- **Facts** — path, type, target framework, presence of `Web.config`/`Global.asax`/`packages.config`, recommended target.
- **Issues** — every finding, grouped by severity (highest first), with a stable ID, description, recommendation, and effort estimate.

### Severity colour coding

| Severity | Meaning | Indicator |
|---|---|---|
| `Blocker` | Cannot be migrated without architectural redesign. | Red border |
| `Error` | Manual rewrite required; no automatic conversion is safe. | Red border |
| `Warning` | Manual review recommended, but a default fix exists. | Amber border |
| `AutoFixable` | Will be auto-handled by the modernizer; surfaced for transparency. | Blue border |
| `Info` | Informational only; no action required. | Grey border |

### Readiness score formula

The score starts at 100 and loses points by issue severity:

| Severity | Penalty |
|---|---|
| Info | 0 |
| AutoFixable | 1 |
| Warning | 3 |
| Error | 8 |
| Blocker | 20 |

Solution score = average of project scores, minus solution-level penalties. The math is intentionally transparent so reports can show exactly why a score is what it is.

---

## Modernizing a `.csproj`

The modernizer takes a legacy verbose `.csproj` and produces an SDK-style equivalent. It handles:

- Replacing the `<Project ToolsVersion="..." xmlns="...">` envelope with `<Project Sdk="Microsoft.NET.Sdk[.Web]">`
- Translating `<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>` → `<TargetFramework>net8.0</TargetFramework>` (or `net8.0-windows` for desktop)
- Migrating every entry from `packages.config` to `<PackageReference>` items
- Preserving project-to-project references
- Choosing the right SDK (`Microsoft.NET.Sdk`, `Microsoft.NET.Sdk.Web`) based on detected project type
- Adding `<UseWindowsForms>` / `<UseWPF>` for desktop projects
- Enabling `Nullable` and `ImplicitUsings` (modern defaults)

**It does not** modify your source code, your `Web.config`, or your `Global.asax`. Those require either Phase 2 (Roslyn rewriter) or manual work.

After running the modernizer:

1. Open the `.modernized.csproj` and review it.
2. Run `dotnet build` against it to surface compile errors.
3. Fix any references and resolve any package version drift.
4. When you're satisfied, replace the original.

---

## Recommended migration workflow

The intended cadence for a real migration:

1. **Run `analyze` on your full solution.** Open the HTML report. Triage:
   - **Blockers** (WebForms, WCF) → these need real planning, not quick fixes. Schedule design discussions.
   - **Errors** (packages with no modern equivalent) → research replacements before starting.
   - **Warnings** → these are the bulk of the work; track them in your issue tracker using the stable issue IDs.
   - **AutoFixable** → these will be handled by the modernizer; nothing to do manually.
2. **Share the Word report with stakeholders.** It's designed for non-technical audiences — readiness score, blocker count, total effort, recommended targets per project. Use it for migration kickoff, status updates, and budget approval.
3. **Migrate from the bottom up.** Start with leaf projects (class libraries, console apps, test projects) — they have fewer dependencies and let you build modernization momentum.
4. **Run `modernize-csproj` on each leaf project.** Review, build, replace the original, commit.
5. **Move up the dependency graph.** As leaf projects modernize, their consumers can follow.
6. **Re-run `analyze` periodically.** Watch the readiness score climb. Use the JSON output in CI to fail builds if blockers are introduced.
7. **Tackle blockers last, deliberately.** WebForms → Blazor or MVC, WCF → CoreWCF or gRPC. These are project-by-project rewrites that need their own planning.

A typical pattern: a 30-project legacy enterprise solution that would take a 4-engineer team 6+ months to migrate manually completes the first 60–70% in 6–8 weeks with NetForward, leaving only the genuinely architectural blockers for human attention.

---

## Issue ID reference

Every finding has a stable ID. IDs are never reused, so reports are diff-able across runs and individual issues can be cross-referenced in your tracker.

| ID | Severity | Title |
|---|---|---|
| `NF001` | AutoFixable | Legacy (non-SDK-style) project format |
| `NF002` | AutoFixable | `packages.config` is present |
| `NF003` | Warning | Targets legacy framework |
| `NF004` | Warning | Missing target framework |
| `NF100` | Warning | `Web.config` present |
| `NF101` | Warning | `Global.asax` present |
| `NF102` | Warning | Custom `HttpModule` |
| `NF103` | Warning | Custom `HttpHandler` |
| `NF200` | Warning | Legacy package reference (with known modern replacement) |
| `NF201` | Error | Package has no modern equivalent |
| `NF300` | Blocker | Web Forms project |
| `NF301` | Blocker | WCF service |
| `NF302` | Warning | ASMX web service |
| `NF303` | Warning | WinForms application |
| `NF304` | Warning | WPF application |

ID ranges:

- `NF001`–`NF099` — project structure
- `NF100`–`NF199` — web configuration
- `NF200`–`NF299` — package references
- `NF300`–`NF399` — project type specific

---

## Extending the compatibility catalog

The mapping database lives in `src/NetForward.Compatibility/Data/*.yaml` and is loaded as embedded resources at runtime. **Adding a new mapping is a YAML edit — no analyzer changes required.**

To add a package mapping:

```yaml
# src/NetForward.Compatibility/Data/packages.yaml
packages:
  - legacyId: SomeOldPackage
    modernId: Some.New.Package
    category: PackageReference
    notes: "Drop-in replacement; API surface is similar."
```

Fields:

| Field | Required | Description |
|---|---|---|
| `legacyId` | yes | Exact NuGet package id used in .NET Framework projects |
| `modernId` | no | Recommended replacement on modern .NET |
| `removedInModern` | no | `true` if no replacement exists; defaults to `false` |
| `category` | no | One of: `PackageReference`, `AspNetApi`, `EntityFramework`, `Identity`, `Logging`, `DependencyInjection`, `Wcf`, `BuildSystem` |
| `notes` | no | Free-text guidance shown in reports |

API mappings work the same way in `apis.yaml`, with `legacyFullName` and `modernFullName`.

After editing, rebuild and the new mappings are live:

```bash
dotnet build
```

---

## Solution structure

```
NetForward/
├── src/
│   ├── NetForward.Core/           Domain models and abstractions (zero deps)
│   ├── NetForward.Analyzer/       Solution & project analysis
│   ├── NetForward.Compatibility/  YAML-backed mapping catalog
│   ├── NetForward.Modernizer/     csproj / web.config / packages.config conversion
│   ├── NetForward.Reporting/      Report formatters (JSON, Markdown, HTML, Word)
│   ├── NetForward.Cli/            Command-line tool (`netforward`)
│   └── NetForward.Dashboard/      Blazor Server dashboard
└── tests/
    ├── NetForward.Tests/          xUnit tests (25+ tests)
    └── TestAssets/                Sample legacy projects used by tests
```

`Core` has no external dependencies. Every other layer references `Core` and either `Analyzer`, `Compatibility`, or both. Reporting is decoupled from analysis through `IReportFormatter`, which makes adding a new format a one-class change.

---

## Build, test, and install

### Standard build

```bash
dotnet restore
dotnet build
dotnet test
```

### Install as a global `dotnet` tool

Once you're happy with a build, package and install:

```bash
dotnet pack src/NetForward.Cli -c Release -o ./nupkg
dotnet tool install --global --add-source ./nupkg NetForward.Cli
```

After install, you can run `netforward` from any directory:

```bash
netforward analyze C:\path\to\Anything.sln
netforward modernize-csproj C:\path\to\Old.csproj
```

To update later:

```bash
dotnet pack src/NetForward.Cli -c Release -o ./nupkg
dotnet tool update --global --add-source ./nupkg NetForward.Cli
```

To uninstall:

```bash
dotnet tool uninstall --global NetForward.Cli
```

---

## Troubleshooting

### "Solution file not found"

Make sure the path exists and is accessible. On Windows, quote the path if it contains spaces:

```bash
netforward analyze "C:\Program Files\My App\App.sln"
```

### "Analyzer threw an exception"

Run with `--verbose` to see the full stack trace. Common causes:

- A `.csproj` referenced by the `.sln` doesn't exist on disk. (NetForward reports this as a solution-level issue rather than crashing — but if you see a hard exception, check this first.)
- A malformed `.csproj` (invalid XML). Open the file in Visual Studio or `xmllint` to confirm.

### A project is misclassified

NetForward classifies projects from a layered set of signals (SDK type, OutputType, package references, assembly references). If your project has unusual conventions and is misclassified, run with `--verbose` and share the top portion of the `.csproj` (`PropertyGroup`, `Reference`/`PackageReference` items) so we can add detection rules.

### The Word report won't open

The OpenXML output is standard-conforming, but corporate antivirus or DLP tools occasionally flag freshly-generated `.docx` files. Try opening with WordPad or LibreOffice to confirm the file itself is fine.

### Dashboard won't start

The default URLs use ports 5000/5001. If those are taken, set `ASPNETCORE_URLS` before running:

```bash
ASPNETCORE_URLS=http://localhost:5050 dotnet run --project src/NetForward.Dashboard
```

---

## FAQ

**Q: Will it modify my source code?**
No. Phase 1 is strictly non-destructive. Even the `modernize-csproj` command writes a new file alongside the original.

**Q: Does it work on .NET Framework 4.8 only, or earlier versions too?**
It analyzes anything from `net20` to `net48` and SDK-style projects targeting `net5.0`+. Older targets (1.x, 2.x) work for analysis but the recommended migration target is always `net8.0`.

**Q: Can it migrate VB.NET projects?**
Phase 1 handles `.csproj` only. `.vbproj` and `.fsproj` are skipped with a notice. VB.NET support is on the roadmap.

**Q: Does it support multi-targeting projects?**
The analyzer reads `<TargetFrameworks>` (plural) too, but Phase 1 picks the first one for classification. Full multi-target support is in Phase 2.

**Q: How do I run this against 100+ solutions in one go?**
Wrap the CLI in a shell loop. The JSON output is designed for aggregation; you can merge multiple `.json` files into a single org-wide dashboard with a small script.

**Q: Will it tell me how to migrate my custom code?**
Phase 1 flags legacy patterns and points you to documentation. Phase 2 introduces a Roslyn-based rewriter that does actual code transformation. Phase 4 layers on AI-assisted suggestions for ambiguous cases.

**Q: Is the output diff-able across runs?**
Yes. Issue IDs are stable, and the JSON output uses deterministic ordering. Commit the JSON to your repo and you can see migration progress over time as a normal git diff.

**Q: Can I use this in CI?**
Yes. The CLI exits with code `10` if blockers are found, which you can use to fail builds. Combine with `--format json` to publish reports as build artifacts.

---

## Roadmap

| Phase | Focus | Status |
|---|---|---|
| **1** | **Analyzer + readiness report + csproj modernizer** | **shipped** |
| 2 | Roslyn rewriter for ASP.NET MVC → ASP.NET Core MVC | planned |
| 3 | MVC → Web API + OpenAPI scaffolding + SPA starter | planned |
| 4 | Desktop advisor + AI augmentation (Anthropic API) | planned |
| 5 | WCF → gRPC, WebForms → Blazor, EF6 → EF Core specialist modules | planned |

For the full technical design, deeper architecture details, and per-phase deliverables, see [TECHNICAL_DESIGN.docx](./docs/TECHNICAL_DESIGN.docx) in this repository.

---

## License

MIT.

## Contributing

Pull requests welcome — particularly for the compatibility catalog. Each new package or API mapping is one YAML entry and improves the tool for everyone.