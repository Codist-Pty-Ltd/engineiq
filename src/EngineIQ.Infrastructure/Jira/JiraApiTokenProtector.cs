using Microsoft.AspNetCore.DataProtection;

namespace EngineIQ.Infrastructure.Jira;

public interface IJiraApiTokenProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedPayload);
}

public sealed class JiraApiTokenProtector : IJiraApiTokenProtector
{
    public const string Purpose = "EngineIQ.JiraConnection.ApiToken.v1";

    private readonly IDataProtector _protector;

    public JiraApiTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedPayload) => _protector.Unprotect(protectedPayload);
}
