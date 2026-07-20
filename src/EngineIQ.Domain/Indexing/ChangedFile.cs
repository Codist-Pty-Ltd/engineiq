namespace EngineIQ.Domain.Indexing;

public enum ChangedFileStatus
{
    Added,
    Modified,
    Removed,
    Renamed
}

/// <summary>A file touched between two commits (or, for a full walk, every indexable file).</summary>
public sealed record ChangedFile(string Path, ChangedFileStatus Status, string? PreviousPath = null);

/// <summary>
/// Result of comparing two commits. <see cref="Truncated"/> is true when the provider's diff was too
/// large to enumerate fully (e.g. GitHub's compare API caps file lists at 300 entries), signalling the
/// caller should fall back to a full re-index instead of trusting <see cref="Files"/> as complete.
/// </summary>
public sealed record CompareResult(IReadOnlyList<ChangedFile> Files, bool Truncated);
