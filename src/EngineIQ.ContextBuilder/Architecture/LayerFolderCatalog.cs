namespace EngineIQ.ContextBuilder.Architecture;

/// <summary>Folder-name tokens per architecture style (aligned with standards YAML layer definitions).</summary>
public static class LayerFolderCatalog
{
    public static readonly IReadOnlyDictionary<string, string[]> Clean =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Domain"] = ["Domain", "Core"],
            ["Application"] = ["Application", "UseCases"],
            ["Infrastructure"] = ["Infrastructure", "Persistence"],
            ["API"] = ["API", "Controllers", "WebAPI"],
        };

    public static readonly IReadOnlyDictionary<string, string[]> Layered =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Presentation"] = ["Presentation", "Web", "UI", "Controllers", "API", "WebAPI"],
            ["Business"] = ["Business", "BLL", "Services", "Application"],
            ["Data"] = ["Data", "DAL", "Persistence", "Infrastructure"],
        };

    public static readonly IReadOnlyDictionary<string, string[]> Hexagonal =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Domain"] = ["Domain", "Core"],
            ["Ports"] = ["Ports", "Port"],
            ["Adapters"] = ["Adapters", "Adapter", "Infrastructure"],
            ["Application"] = ["Application", "UseCases"],
        };

    public static readonly IReadOnlyDictionary<string, string[]> ModularMonolith =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Domain"] = ["Domain", "Core"],
            ["Application"] = ["Application", "UseCases"],
            ["Infrastructure"] = ["Infrastructure", "Persistence"],
            ["API"] = ["API", "Host", "Web"],
        };

    public static IReadOnlyDictionary<string, string[]> ForStyle(string style) =>
        style switch
        {
            Domain.Context.ArchitectureStyles.Clean => Clean,
            Domain.Context.ArchitectureStyles.Layered => Layered,
            Domain.Context.ArchitectureStyles.Hexagonal => Hexagonal,
            Domain.Context.ArchitectureStyles.ModularMonolith => ModularMonolith,
            _ => Clean,
        };
}
