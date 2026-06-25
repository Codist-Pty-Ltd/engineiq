namespace EngineIQ.Domain.Configuration;

public sealed class EngineIQDashboardOptions
{
    public const string SectionName = "EngineIQ";

    public string DashboardBaseUrl { get; set; } = "https://app.engineiq.co.za";
}
