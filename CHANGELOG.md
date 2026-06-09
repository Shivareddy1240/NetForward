# Changelog

All notable changes to NetForward are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [0.2.0] — 2026-05 — Phase 2 complete

### Added — Roslyn rewriter (NetForward.Rewriter)

**Tier 1 rules (auto-apply, >95% safe):**
- **R001** Namespace rewriter: `System.Web.Mvc` → `Microsoft.AspNetCore.Mvc`, `System.Web.Http`, `System.Web.Routing`, `System.Web.Security`, `System.Configuration` and all sub-namespaces.
- **R002** Controller base class swap: `ApiController` → `ControllerBase` + `[ApiController]` + `[Route("[controller]")]`.
- **R003** Action result type mapping: `IHttpActionResult` → `IActionResult`, `HttpNotFound()` → `NotFound()`, `HttpBadRequest()` → `BadRequest()`, `InternalServerError()` → `StatusCode(500)`, `new HttpStatusCodeResult(x)` → `StatusCode(x)`, and more.
- **R004** Attribute conversions: `[FromUri]` → `[FromQuery]`. Removes `[ChildActionOnly]`, `[RequireHttps]`, `[HandleError]`, `[OutputCache]` with issue raised per removal (NF404–NF406).
- **R005** RoutePrefix consolidation: `[RoutePrefix("api/orders")]` on class → `[Route("api/orders")]` (ASP.NET Core has no RoutePrefix).
- **R006** Deep `IHttpActionResult` pass: catches `Task<IHttpActionResult>` async actions missed by R003. Flags `ResponseMessage()` as NF408 and `InternalServerError(exception)` as NF409.

**Tier 2 rules (apply with warnings, human review recommended):**
- **R007** DI retrofit: `DependencyResolver.Current.GetService<T>()` and `ServiceLocator.Current.GetInstance<T>()` → generates private readonly field, constructor parameter, and assignment. Two-pass Collector + Rewriter approach. Static method usages flagged NF400.
- **R008** `HttpContext.Current` → `IHttpContextAccessor`: injects `IHttpContextAccessor` into constructor, adds field, replaces all access sites. Static method usages flagged NF401.
- **R009** Model binding patterns: `[Bind(Include="...")]` → positional `[Bind("...")]`, `FormCollection` → `IFormCollection`, `HttpPostedFileBase` → `IFormFile`, `HttpPostedFilesBase` → `IFormFileCollection`, `UpdateModel()`/`TryUpdateModel()` flagged NF402.

**Tier 3 rules (flag only — no source modification):**
- **R010** Action filter advisor: flags `ActionFilterAttribute`, `AuthorizationFilterAttribute`, `ExceptionFilterAttribute`, `ResultFilterAttribute` subclasses and `IActionFilter`/`IAuthorizationFilter` implementations with Core signature guidance (NF450/NF451/NF452/NF453).
- **R011** HttpModule/Handler advisor: flags `IHttpModule` implementations with a concrete middleware stub in the recommendation. Flags `IHttpHandler`/`IHttpAsyncHandler` with a minimal API endpoint stub (NF452/NF453).

**Pipeline infrastructure:**
- `RewritePipeline`: orchestrates all rules tier-by-tier, handles side-by-side output, optionally runs `dotnet build` verification.
- `RewriteContext.GetSemanticModel()`: automatically replaces stale trees in the compilation when rules rewrite between steps — prevents the "SyntaxTree is not part of the compilation" error when chaining rules.
- `MigrationCompilationVerifier`: runs `dotnet build` against the migrated output, parses MSBuild diagnostics into `MigrationIssue` instances (NF5xxx range), includes per-error-code remediation guidance.
- `MSBuildWorkspaceLoader`: wraps `Microsoft.Build.Locator` initialization — safe to call multiple times.

**New CLI verb:**
- `netforward migrate <SOLUTION.sln>` with `--dry-run`, `--no-verify`, `--tier`, `--suffix`, `--output` options.

**New dashboard page:**
- `/migrate`: solution path input, dry-run/verify/tier checkboxes, per-project results with expandable transformation list, build status badge, remaining issues, compile errors.

**Config converters:**
- `WebConfigConverter`: `<appSettings>` → camelCase JSON, `<connectionStrings>` → `ConnectionStrings` section (PascalCase preserved for IConfiguration compatibility), `<system.net><mailSettings>` → `email` section, unknown sections → TODO stubs.
- `GlobalAsaxConverter`: `Application_Start` body → comment in `builder.Services` block, `Application_Error` → exception handler, `BeginRequest`/`EndRequest` → middleware stub comment, `Session_Start` → `AddSession()` hint, `Application_End` → `IHostApplicationLifetime` hint.

### Changed
- `NetForward.Dashboard.csproj`: added `NetForward.Rewriter` and `NetForward.Converters` references.
- `site.css`: added migration page styles, build badge, transformation list, rule ID badge.
- `MainLayout.razor`: added Migrate nav link.

### Fixed
- JSON report formatter now emits camelCase property names (`readinessScore` not `ReadinessScore`).
- `CsprojParser`, `PackagesConfigParser`, `ParsedCsproj`, `ParsedPackageReference` changed from `internal` to `public` to allow cross-assembly access from `NetForward.Modernizer`.
- `MigrationCompilationVerifier` namespace changed to `NetForward.Rewriter.Verification` (was `.Compilation`) to avoid clash with Roslyn's `Compilation` type.
- `BuildVerificationResult` constructor uses object initializer syntax (not positional) matching the record definition.
- R006: `VisitGenericName` now registers suppression before calling `base.VisitGenericName` to prevent `VisitIdentifierName` double-firing on `Task<IHttpActionResult>` type arguments.
- R009 `IsApplicable`: checks for bare `"FormCollection"` not preceded by `"I"` to avoid false-positive on `IFormCollection` (the modern type).
- R007/R008 generated fields and constructors: explicit trailing-space trivia on modifier tokens to prevent squashed output like `privatereadonlyIOrderService_orderService`.
- R008: `StaticMethodIssues` now merged into the returned `Issues` list so `NF401` is surfaced even when no rewriting occurs.

---

## [0.1.0] — 2026-04 — Phase 1 complete

### Added
- `SolutionAnalyzer` and `ProjectAnalyzer`: parse `.sln` and `.csproj` (both legacy verbose and SDK-style), classify project types, inventory packages, detect 14 categories of legacy patterns.
- YAML-backed `CompatibilityCatalog` (27 package mappings, 22 API mappings).
- Transparent readiness scoring (0–100) with severity-weighted issue penalties.
- Per-issue effort estimation in hours.
- Four report formatters: JSON (camelCase), Markdown, HTML (self-contained), Word (OpenXML).
- `CsprojModernizer`: legacy verbose `.csproj` → SDK-style, `packages.config` → `<PackageReference>`.
- CLI verbs: `netforward analyze`, `netforward modernize-csproj`.
- Blazor Server dashboard: `/` analyze page.
- 25+ xUnit tests with real legacy MVC 5 test asset.
