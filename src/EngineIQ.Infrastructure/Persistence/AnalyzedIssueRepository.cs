using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Messaging;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace EngineIQ.Infrastructure.Persistence;

public sealed class AnalyzedIssueRepository : IAnalyzedIssueRepository
{
    private readonly IDbContextFactory<EngineIQDbContext> _factory;

    public AnalyzedIssueRepository(IDbContextFactory<EngineIQDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<AnalyzedIssueRow?> GetByIssueAsync(
        Guid tenantId,
        Guid jiraConnectionId,
        long jiraIssueId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        var row = await db.AnalyzedIssues.AsNoTracking()
            .Where(a =>
                a.TenantId == tenantId
                && a.JiraConnectionId == jiraConnectionId
                && a.JiraIssueId == jiraIssueId)
            .Select(a => new
            {
                a.Id,
                a.JiraConnectionId,
                a.JiraIssueId,
                a.IssueKey,
                a.JiraCommentId,
                a.LastAnalyzedIssueUpdatedAt,
                a.LastTrigger,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return null;

        var trigger = Enum.TryParse<AnalysisTrigger>(row.LastTrigger, ignoreCase: true, out var parsed)
            ? parsed
            : AnalysisTrigger.Created;

        return new AnalyzedIssueRow(
            row.Id,
            row.JiraConnectionId,
            row.JiraIssueId,
            row.IssueKey,
            row.JiraCommentId,
            row.LastAnalyzedIssueUpdatedAt,
            trigger);
    }

    public async Task UpsertAsync(
        Guid tenantId,
        Guid jiraConnectionId,
        long jiraIssueId,
        string issueKey,
        string jiraCommentId,
        DateTimeOffset lastAnalyzedIssueUpdatedAt,
        AnalysisTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        var existing = await db.AnalyzedIssues
            .FirstOrDefaultAsync(
                a => a.TenantId == tenantId
                     && a.JiraConnectionId == jiraConnectionId
                     && a.JiraIssueId == jiraIssueId,
                cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var triggerText = trigger.ToString();

        if (existing is null)
        {
            db.AnalyzedIssues.Add(new AnalyzedIssue
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                JiraConnectionId = jiraConnectionId,
                JiraIssueId = jiraIssueId,
                IssueKey = issueKey,
                JiraCommentId = jiraCommentId,
                LastAnalyzedIssueUpdatedAt = lastAnalyzedIssueUpdatedAt,
                LastTrigger = triggerText,
                UpdatedAt = now,
            });
        }
        else
        {
            existing.IssueKey = issueKey;
            existing.JiraCommentId = jiraCommentId;
            existing.LastAnalyzedIssueUpdatedAt = lastAnalyzedIssueUpdatedAt;
            existing.LastTrigger = triggerText;
            existing.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
