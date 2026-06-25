using System.Reflection;
using System.Text;
using YamlDotNet.Serialization;

namespace EngineIQ.StandardsEngine.Config;

public static class StandardsConfigLoader
{
    private static readonly Lazy<string> DefaultYaml = new(LoadEmbeddedDefaultYaml);

    public static StandardsConfigDocument Load(string? standardsConfigYaml)
    {
        var yaml = string.IsNullOrWhiteSpace(standardsConfigYaml) ? DefaultYaml.Value : standardsConfigYaml;
        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();

        var doc = deserializer.Deserialize<StandardsConfigDocument>(yaml)
                  ?? new StandardsConfigDocument();
        doc.Rules ??= [];
        return doc;
    }

    private static string LoadEmbeddedDefaultYaml()
    {
        var assembly = typeof(StandardsConfigLoader).Assembly;
        const string resourceName = "EngineIQ.StandardsEngine.Defaults.clean-architecture.yaml";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException($"Embedded standards default not found: {resourceName}");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
