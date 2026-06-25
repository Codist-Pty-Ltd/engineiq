namespace EngineIQ.Infrastructure;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>e.g. localhost:6379 or redis:6379</summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    public int ContextCacheTtlHours { get; set; } = 24;
}
