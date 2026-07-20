namespace EngineIQ.ContextBuilder.Indexing;

/// <summary>A chunk of text before the content hash is computed (added by <see cref="CompositeCodeChunker"/>).</summary>
public sealed record RawChunk(string Content, int StartLine, int EndLine, string? SymbolName = null, string? Kind = null);
