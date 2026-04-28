using NetForward.Analyzer;
using NetForward.Compatibility;
using NetForward.Core.Abstractions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Register NetForward services as singletons. The catalog loads YAML at construction;
// analyzers are pure services with no per-request state.
builder.Services.AddSingleton<ICompatibilityCatalog, YamlCompatibilityCatalog>();
builder.Services.AddSingleton<IProjectAnalyzer, ProjectAnalyzer>();
builder.Services.AddSingleton<ISolutionAnalyzer, SolutionAnalyzer>();
builder.Services.AddSingleton<IAiAdvisor, NullAiAdvisor>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
