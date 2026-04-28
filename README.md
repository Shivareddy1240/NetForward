# NetForward

A migration toolkit for legacy .NET Framework applications. Analyzes solutions, scores migration readiness, and modernizes project files automatically.

## Phase 1 status (current)

Phase 1 ships the foundation:

- **Analyzer** — scans `.sln` and `.csproj` files, classifies projects (MVC, Web API, WebForms, WCF, WinForms, WPF, …), detects legacy package references, and produces a structured readiness report.
- **CSProj modernizer** — converts legacy verbose `.csproj` to modern SDK-style format, merging `packages.config` into `<PackageReference>` items.
- **Compatibility catalog** — YAML-backed, contributable database of legacy → modern API and package mappings.
- **Report formatters** — JSON (machine-readable), Markdown (human-readable), HTML (self-contained dashboard), and Word (stakeholder-ready).
- **CLI** — `netforward analyze` and `netforward modernize-csproj` verbs.
- **Blazor dashboard** — interactive web UI for running analyses.

Phase 1 is **non-destructive**: nothing in the source solution is modified. All output goes to a separate directory.

## Roadmap

| Phase | Focus | Status |
|---|---|---|
| 1 | Analyzer + readiness report + csproj modernizer | **shipped** |
| 2 | Roslyn rewriter for ASP.NET MVC → ASP.NET Core MVC | planned |
| 3 | MVC → Web API + OpenAPI scaffolding + SPA starter | planned |
| 4 | Desktop advisor + AI augmentation (Anthropic API) | planned |
| 5 | WCF → gRPC, WebForms → Blazor, EF6 → EF Core specialist modules | planned |

## Solution layout

```
NetForward/
├── src/
│   ├── NetForward.Core/           # Domain models and abstractions (no dependencies)
│   ├── NetForward.Analyzer/       # Solution & project analysis
│   ├── NetForward.Compatibility/  # YAML-backed mapping catalog
│   ├── NetForward.Modernizer/     # csproj / web.config / packages.config conversion
│   ├── NetForward.Reporting/      # Report formatters (JSON, Markdown, HTML, Word)
│   ├── NetForward.Cli/            # Command-line tool (`netforward`)
│   └── NetForward.Dashboard/      # Blazor Server dashboard
└── tests/
    ├── NetForward.Tests/          # xUnit tests
    └── TestAssets/                # Sample legacy projects used by tests
```

`Core` has no dependencies. Every other layer references `Core` and either `Analyzer`, `Compatibility`, or both. Reporting is decoupled from analysis through `IReportFormatter`, which makes adding a new format a one-class change.

## Quick start

```bash
# build
dotnet build NetForward.sln

# analyze a legacy solution
dotnet run --project src/NetForward.Cli -- analyze path/to/Legacy.sln

# only the formats you want
dotnet run --project src/NetForward.Cli -- analyze path/to/Legacy.sln --format markdown,json

# convert a single legacy csproj to SDK-style
dotnet run --project src/NetForward.Cli -- modernize-csproj path/to/Legacy.csproj

# run the dashboard
dotnet run --project src/NetForward.Dashboard
# open http://localhost:5000

# run tests
dotnet test
```

## Issue ID scheme

Every finding has a stable identifier (e.g. `NF001`) so reports are diff-able across runs and individual issues can be suppressed. IDs are never reused. The current registry lives in `NetForward.Analyzer/IssueIds.cs`.

| Range | Category |
|---|---|
| `NF001`–`NF099` | Project structure |
| `NF100`–`NF199` | Web configuration |
| `NF200`–`NF299` | Package references |
| `NF300`–`NF399` | Project type specific |

## Readiness scoring

Per-project scores start at 100 and lose points by issue severity:

| Severity | Penalty |
|---|---|
| Info | 0 |
| AutoFixable | 1 |
| Warning | 3 |
| Error | 8 |
| Blocker | 20 |

Solution score = average of project scores, minus solution-level penalties.

The math is intentionally transparent: every report shows the issues that contributed to the score so users can see exactly why their score is what it is.

## Contributing to the compatibility catalog

The mappings live in `src/NetForward.Compatibility/Data/*.yaml` and are loaded as embedded resources at runtime. Adding a new mapping is just a YAML edit — no recompilation of analyzer logic required.

```yaml
- legacyId: SomeOldPackage
  modernId: Some.New.Package
  category: PackageReference
  notes: "Drop-in replacement; API surface is similar."
```

## License

MIT.
