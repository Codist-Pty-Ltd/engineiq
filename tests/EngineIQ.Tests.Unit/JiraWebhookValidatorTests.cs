using EngineIQ.Jira;

namespace EngineIQ.Tests.Unit;

public class JiraWebhookValidatorTests
{
    [Fact]
    public void SecretsEqual_returns_true_for_identical_strings()
    {
        Assert.True(JiraWebhookValidator.SecretsEqual("abc123", "abc123"));
    }

    [Fact]
    public void SecretsEqual_returns_false_for_different_strings()
    {
        Assert.False(JiraWebhookValidator.SecretsEqual("abc123", "abc124"));
    }

    [Fact]
    public void SecretsEqual_returns_false_for_different_lengths()
    {
        Assert.False(JiraWebhookValidator.SecretsEqual("short", "longer-value"));
    }

    [Fact]
    public void SecretsEqual_treats_null_as_empty()
    {
        Assert.True(JiraWebhookValidator.SecretsEqual(null!, null!));
        Assert.False(JiraWebhookValidator.SecretsEqual(null!, "x"));
    }
}
