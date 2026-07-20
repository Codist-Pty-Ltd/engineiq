namespace EngineIQ.ContextBuilder.Indexing;

/// <summary>
/// Generic line-based chunker for non-C# text files (and the Roslyn chunker's fallback): fixed-size
/// windows with overlap so a boundary doesn't split related context out of every chunk.
/// </summary>
public sealed class SlidingWindowChunker
{
    private const int WindowLines = 60;
    private const int OverlapLines = 15;
    private const int MinNonEmptyLines = 5;
    private const int StepLines = WindowLines - OverlapLines;

    public IReadOnlyList<RawChunk> Chunk(string content)
    {
        if (string.IsNullOrEmpty(content))
            return Array.Empty<RawChunk>();

        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var chunks = new List<RawChunk>();

        for (var start = 0; start < lines.Length; start += StepLines)
        {
            var end = Math.Min(start + WindowLines, lines.Length);
            var window = lines[start..end];
            var nonEmpty = window.Count(l => !string.IsNullOrWhiteSpace(l));
            if (nonEmpty >= MinNonEmptyLines)
            {
                chunks.Add(new RawChunk(
                    string.Join('\n', window),
                    start + 1,
                    end));
            }

            if (end >= lines.Length)
                break;
        }

        return chunks;
    }
}
