using YamlDotNet.Serialization;

namespace EngineIQ.StandardsEngine.Config;

public sealed class StandardsConfigDocument
{
    [YamlMember(Alias = "rules")]
    public List<StandardsRuleDefinition> Rules { get; set; } = [];
}

public sealed class StandardsRuleDefinition
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = string.Empty;

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "severity")]
    public string Severity { get; set; } = "high";

    [YamlMember(Alias = "check")]
    public string Check { get; set; } = string.Empty;
}
