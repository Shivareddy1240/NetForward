using Microsoft.Extensions.Logging;
using NetForward.Core.Abstractions;
using NetForward.Rewriter.Rewriters;

namespace NetForward.Rewriter.Pipeline;

/// <summary>
/// Builds a fully configured <see cref="RewritePipeline"/> with all rules registered.
/// Rules are applied in tier order, then by ID within each tier.
/// Adding a new rule is a one-line change here — no pipeline logic changes.
/// </summary>
public static class RewriterFactory
{
    public static RewritePipeline CreateDefault(
        ICompatibilityCatalog catalog,
        ILoggerFactory? loggerFactory = null)
    {
        var rules = new IRewriteRule[]
        {
            // === Tier 1 — Sprint 1 ===
            new R001NamespaceRewriter(),
            new R002ControllerBaseRewriter(),
            new R003ActionResultRewriter(),

            // === Tier 1 — Sprint 2 ===
            new R004AttributeConversionRewriter(),
            new R005RoutePrefixRewriter(),
            new R006IHttpActionResultRewriter(),

            // === Tier 2 — Sprint 3 ===
            new R007DependencyInjectionRewriter(),
            new R008HttpContextRewriter(),
            new R009ModelBindingRewriter(),

            // === Tier 3 — Sprint 4 (flag-only advisors, planned) ===
            // new R010ActionFilterAdvisor(),
            // new R011HttpModuleAdvisor(),
        };

        var logger = loggerFactory?.CreateLogger<RewritePipeline>();
        return new RewritePipeline(rules, catalog, logger);
    }
}
