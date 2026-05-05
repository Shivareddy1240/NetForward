using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace NetForward.Converters.WebConfig;

/// <summary>
/// Converts a legacy web.config to appsettings.json + appsettings.Development.json.
///
/// Key design decisions:
/// - appSettings keys are camelCased (conventional for JSON).
/// - "ConnectionStrings" section key stays PascalCase — ASP.NET Core's
///   IConfiguration.GetConnectionString() requires this exact casing.
/// - Dictionary keys are camelCased manually because JsonNamingPolicy
///   does NOT apply to Dictionary&lt;string,object&gt; keys.
/// </summary>
public sealed class WebConfigConverter
{
    public sealed record ConversionResult(
        string AppSettingsPath,
        string AppSettingsContent,
        string DevSettingsPath,
        string DevSettingsContent,
        IReadOnlyList<string> Notes);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    // Keys that must stay PascalCase for ASP.NET Core convention compatibility.
    private static readonly HashSet<string> PascalCaseExemptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ConnectionStrings",
        "Logging",
        "AllowedHosts"
    };

    public ConversionResult Convert(string webConfigPath, string outputDirectory)
    {
        if (!File.Exists(webConfigPath))
            throw new FileNotFoundException($"web.config not found: {webConfigPath}", webConfigPath);

        var doc = XDocument.Load(webConfigPath);
        var root = doc.Root ?? throw new InvalidOperationException("web.config has no root element.");

        var appSettingsDict = new Dictionary<string, object>(StringComparer.Ordinal);
        var devSettingsDict = new Dictionary<string, object>(StringComparer.Ordinal);
        var notes = new List<string>();

        // ---- <appSettings> -------------------------------------------------
        var appSettingsEl = root.Element("appSettings");
        if (appSettingsEl is not null)
        {
            int count = 0;
            foreach (var add in appSettingsEl.Elements("add"))
            {
                var key = add.Attribute("key")?.Value ?? "";
                var value = add.Attribute("value")?.Value ?? "";
                if (!string.IsNullOrWhiteSpace(key))
                {
                    appSettingsDict[NormalizeKey(key)] = value;
                    count++;
                }
            }
            notes.Add($"Migrated {count} <appSettings> entries.");
        }

        // ---- <connectionStrings> -------------------------------------------
        // "ConnectionStrings" stays PascalCase — IConfiguration requires it.
        var connStrEl = root.Element("connectionStrings");
        if (connStrEl is not null)
        {
            var connStrings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var add in connStrEl.Elements("add"))
            {
                var name = add.Attribute("name")?.Value ?? "";
                var connStr = add.Attribute("connectionString")?.Value ?? "";
                if (!string.IsNullOrWhiteSpace(name))
                    connStrings[name] = connStr;
            }

            if (connStrings.Count > 0)
            {
                appSettingsDict["ConnectionStrings"] = connStrings;
                devSettingsDict["ConnectionStrings"] = connStrings;
                notes.Add($"Migrated {connStrings.Count} <connectionStrings> entry/entries.");
                notes.Add("Connection strings copied to appsettings.Development.json — review before committing.");
            }
        }

        // ---- <system.net><mailSettings> ------------------------------------
        var mailEl = root
            .Element("system.net")
            ?.Element("mailSettings")
            ?.Element("smtp");

        if (mailEl is not null)
        {
            var emailSection = new Dictionary<string, object>(StringComparer.Ordinal);
            var fromAttr = mailEl.Attribute("from")?.Value;
            if (fromAttr is not null) emailSection["from"] = fromAttr;

            var networkEl = mailEl.Element("network");
            if (networkEl is not null)
            {
                emailSection["host"] = networkEl.Attribute("host")?.Value ?? "";
                emailSection["port"] = int.TryParse(networkEl.Attribute("port")?.Value, out var port) ? port : 25;
                emailSection["userName"] = networkEl.Attribute("userName")?.Value ?? "";
                emailSection["password"] = networkEl.Attribute("password")?.Value ?? "";
                emailSection["enableSsl"] = bool.TryParse(networkEl.Attribute("enableSsl")?.Value, out var ssl) && ssl;
            }

            appSettingsDict["email"] = emailSection;
            notes.Add("Migrated <system.net><mailSettings> to email section.");
        }

        // ---- Unknown custom sections → TODO stubs --------------------------
        var knownSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "configSections", "appSettings", "connectionStrings",
            "system.web", "system.webServer", "system.net",
            "runtime", "startup", "entityFramework",
            "log4net", "nlog"
        };

        foreach (var el in root.Elements())
        {
            if (knownSections.Contains(el.Name.LocalName)) continue;

            var stubKey = $"TODO_{el.Name.LocalName}";
            appSettingsDict[stubKey] =
                $"// Migrate manually — original: {el.ToString().Replace("\r\n", " ").Replace("\n", " ")}";

            notes.Add($"Section <{el.Name.LocalName}> could not be auto-migrated. A TODO stub was added.");
        }

        // ---- Serialize -------------------------------------------------------
        var appSettingsContent = JsonSerializer.Serialize(appSettingsDict, Options);
        var devSettingsContent = JsonSerializer.Serialize(devSettingsDict, Options);

        Directory.CreateDirectory(outputDirectory);
        var appSettingsPath = Path.Combine(outputDirectory, "appsettings.json");
        var devSettingsPath = Path.Combine(outputDirectory, "appsettings.Development.json");

        File.WriteAllText(appSettingsPath, appSettingsContent, Encoding.UTF8);
        File.WriteAllText(devSettingsPath, devSettingsContent, Encoding.UTF8);

        notes.Add($"appsettings.json written to: {appSettingsPath}");
        notes.Add($"appsettings.Development.json written to: {devSettingsPath}");

        return new ConversionResult(
            appSettingsPath,
            appSettingsContent,
            devSettingsPath,
            devSettingsContent,
            notes);
    }

    /// <summary>
    /// Normalize a config key: camelCase unless it's a known PascalCase convention.
    /// </summary>
    private static string NormalizeKey(string key)
    {
        if (PascalCaseExemptions.Contains(key)) return key;
        return ToCamelCase(key);
    }

    /// <summary>
    /// Convert a string to camelCase.
    /// "ApiBaseUrl" → "apiBaseUrl"
    /// "my_key"     → "myKey"
    /// "MY-KEY"     → "mYKEY"  (ALL_CAPS not supported — use snake_case for those)
    /// </summary>
    public static string ToCamelCase(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;

        var parts = key.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return key;

        var sb = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (string.IsNullOrEmpty(part)) continue;

            if (i == 0)
            {
                sb.Append(char.ToLowerInvariant(part[0]));
                if (part.Length > 1) sb.Append(part[1..]);
            }
            else
            {
                sb.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1) sb.Append(part[1..]);
            }
        }

        return sb.ToString();
    }
}
