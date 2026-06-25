namespace EngineIQ.Domain.Context;

/// <summary>Detected repository architecture for standards rules and AI prompt enrichment.</summary>
public sealed record RepoContext(
    string DetectedStyle,
    Dictionary<string, List<string>> LayerFolderMap,
    List<string> NotablePatterns,
    DateTimeOffset IndexedAt);
