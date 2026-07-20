using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EngineIQ.Infrastructure.Migrations;

/// <inheritdoc />
public partial class Session15_BacklogInjection : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "trigger",
            schema: "public",
            table: "issue_analysis_jobs",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "analyzed_issues",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                jira_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                jira_issue_id = table.Column<long>(type: "bigint", nullable: false),
                issue_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                jira_comment_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                last_analyzed_issue_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_analyzed_issues", x => x.id);
                table.ForeignKey(
                    name: "fk_analyzed_issues_jira_connections_jira_connection_id",
                    column: x => x.jira_connection_id,
                    principalSchema: "public",
                    principalTable: "jira_connections",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_analyzed_issues_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "public",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "backlog_backfills",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                jira_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                jql = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                start_at_cursor = table.Column<int>(type: "integer", nullable: false),
                matched_total = table.Column<int>(type: "integer", nullable: false),
                enqueued_count = table.Column<int>(type: "integer", nullable: false),
                skipped_count = table.Column<int>(type: "integer", nullable: false),
                max_issues = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                failure_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_backlog_backfills", x => x.id);
                table.ForeignKey(
                    name: "fk_backlog_backfills_jira_connections_jira_connection_id",
                    column: x => x.jira_connection_id,
                    principalSchema: "public",
                    principalTable: "jira_connections",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_backlog_backfills_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "public",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_analyzed_issues_jira_connection_id_jira_issue_id",
            schema: "public",
            table: "analyzed_issues",
            columns: new[] { "jira_connection_id", "jira_issue_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_analyzed_issues_tenant_id_jira_connection_id",
            schema: "public",
            table: "analyzed_issues",
            columns: new[] { "tenant_id", "jira_connection_id" });

        migrationBuilder.CreateIndex(
            name: "ix_backlog_backfills_jira_connection_id",
            schema: "public",
            table: "backlog_backfills",
            column: "jira_connection_id");

        migrationBuilder.CreateIndex(
            name: "ix_backlog_backfills_tenant_id_jira_connection_id_status",
            schema: "public",
            table: "backlog_backfills",
            columns: new[] { "tenant_id", "jira_connection_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_backlog_backfills_tenant_id_status",
            schema: "public",
            table: "backlog_backfills",
            columns: new[] { "tenant_id", "status" });

        migrationBuilder.Sql(
            """
            ALTER TABLE public.analyzed_issues ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.analyzed_issues FORCE ROW LEVEL SECURITY;
            CREATE POLICY analyzed_issues_tenant_ctx ON public.analyzed_issues
                FOR ALL
                TO PUBLIC
                USING (tenant_id = NULLIF(trim(current_setting('app.current_tenant_id', true)), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(trim(current_setting('app.current_tenant_id', true)), '')::uuid);

            ALTER TABLE public.backlog_backfills ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.backlog_backfills FORCE ROW LEVEL SECURITY;
            CREATE POLICY backlog_backfills_tenant_ctx ON public.backlog_backfills
                FOR ALL
                TO PUBLIC
                USING (tenant_id = NULLIF(trim(current_setting('app.current_tenant_id', true)), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(trim(current_setting('app.current_tenant_id', true)), '')::uuid);

            CREATE OR REPLACE FUNCTION public.fn_list_stale_pending_backfill_jobs(p_cutoff timestamptz, p_limit int)
            RETURNS TABLE (
                tenant_id uuid,
                job_id uuid,
                jira_connection_id uuid
            )
            LANGUAGE sql
            STABLE
            SECURITY DEFINER
            SET search_path = public
            AS $func$
                SELECT
                    j.tenant_id,
                    j.id AS job_id,
                    j.jira_connection_id
                FROM public.backlog_backfills AS j
                WHERE j.status = 'PendingPublish'
                  AND j.created_at <= p_cutoff
                ORDER BY j.created_at
                LIMIT GREATEST(1, LEAST(COALESCE(p_limit, 50), 200));
            $func$;

            COMMENT ON FUNCTION public.fn_list_stale_pending_backfill_jobs(timestamptz, int) IS
                'Lists stale PendingPublish backlog backfill jobs across tenants (bypasses FORCE RLS).';

            CREATE OR REPLACE FUNCTION public.fn_list_stale_pending_jira_jobs(p_cutoff timestamptz, p_limit int)
            RETURNS TABLE (
                tenant_id uuid,
                job_id uuid,
                jira_connection_id uuid,
                issue_key text,
                jira_issue_id bigint,
                "trigger" text
            )
            LANGUAGE sql
            STABLE
            SECURITY DEFINER
            SET search_path = public
            AS $func$
                SELECT
                    j.tenant_id,
                    j.id AS job_id,
                    j.jira_connection_id,
                    j.issue_key::text,
                    j.jira_issue_id,
                    j."trigger"::text
                FROM public.issue_analysis_jobs AS j
                WHERE j.status = 'PendingPublish'
                  AND j.created_at <= p_cutoff
                ORDER BY j.created_at
                LIMIT GREATEST(1, LEAST(COALESCE(p_limit, 50), 200));
            $func$;

            COMMENT ON FUNCTION public.fn_list_stale_pending_jira_jobs(timestamptz, int) IS
                'Lists stale PendingPublish Jira issue-analysis jobs across tenants (bypasses FORCE RLS).';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP FUNCTION IF EXISTS public.fn_list_stale_pending_backfill_jobs(timestamptz, int);

            CREATE OR REPLACE FUNCTION public.fn_list_stale_pending_jira_jobs(p_cutoff timestamptz, p_limit int)
            RETURNS TABLE (
                tenant_id uuid,
                job_id uuid,
                jira_connection_id uuid,
                issue_key text,
                jira_issue_id bigint
            )
            LANGUAGE sql
            STABLE
            SECURITY DEFINER
            SET search_path = public
            AS $func$
                SELECT
                    j.tenant_id,
                    j.id AS job_id,
                    j.jira_connection_id,
                    j.issue_key::text,
                    j.jira_issue_id
                FROM public.issue_analysis_jobs AS j
                WHERE j.status = 'PendingPublish'
                  AND j.created_at <= p_cutoff
                ORDER BY j.created_at
                LIMIT GREATEST(1, LEAST(COALESCE(p_limit, 50), 200));
            $func$;

            DROP POLICY IF EXISTS backlog_backfills_tenant_ctx ON public.backlog_backfills;
            ALTER TABLE public.backlog_backfills NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.backlog_backfills DISABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS analyzed_issues_tenant_ctx ON public.analyzed_issues;
            ALTER TABLE public.analyzed_issues NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.analyzed_issues DISABLE ROW LEVEL SECURITY;
            """);

        migrationBuilder.DropTable(
            name: "analyzed_issues",
            schema: "public");

        migrationBuilder.DropTable(
            name: "backlog_backfills",
            schema: "public");

        migrationBuilder.DropColumn(
            name: "trigger",
            schema: "public",
            table: "issue_analysis_jobs");
    }
}
