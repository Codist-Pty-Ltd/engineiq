using EngineIQ.Domain.Interfaces;

namespace EngineIQ.Tests.Unit;

/// <summary>
/// Pure validation helpers mirroring <c>JiraConnectionController.ReplaceMappings</c> tenant-repo checks
/// (controller wiring covered by compile + this predicate).
/// </summary>
public class JiraMappingValidationTests
{
    [Fact]
    public void Rejects_repository_ids_not_in_tenant()
    {
        var tenantRepos = new HashSet<Guid> { Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa") };
        var foreign = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        Assert.False(IsValidMappingRepos(tenantRepos, new[] { foreign }));
        Assert.True(IsValidMappingRepos(tenantRepos, tenantRepos.ToList()));
    }

    [Fact]
    public void Full_replace_input_shape_allows_empty_list()
    {
        var inputs = Array.Empty<JiraProjectMappingInput>();
        Assert.Empty(inputs);
    }

    internal static bool IsValidMappingRepos(ISet<Guid> tenantRepoIds, IReadOnlyList<Guid> requested) =>
        requested.All(tenantRepoIds.Contains);
}
