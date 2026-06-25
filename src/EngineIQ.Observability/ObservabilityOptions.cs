namespace EngineIQ.Observability;

/// <summary>
/// Observability settings. We use Prometheus pull scrape on loopback-only HTTP endpoints
/// (lighter than running an OTLP collector on a single Hetzner VPS). Bind metrics to
/// 127.0.0.1 and scrape with Prometheus or Grafana Agent on the host — never expose via Caddy.
/// </summary>
public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public bool Enabled { get; set; } = true;

    /// <summary>Loopback port for GET /metrics (API default 9464, Worker default 9465).</summary>
    public int MetricsPort { get; set; } = 9464;
}
