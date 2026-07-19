using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EngineIQ.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Session12_JiraIssueAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "issues_analyzed",
                schema: "public",
                table: "tenant_metrics",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "jira_connections",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_base_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    api_token_protected = table.Column<string>(type: "text", nullable: false),
                    webhook_secret = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    project_keys_csv = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jira_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_jira_connections_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "public",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "issue_analysis_jobs",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jira_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issue_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    jira_issue_id = table.Column<long>(type: "bigint", nullable: false),
                    dedupe_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    input_tokens = table.Column<int>(type: "integer", nullable: false),
                    output_tokens = table.Column<int>(type: "integer", nullable: false),
                    estimated_cost_zar = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_analysis_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_issue_analysis_jobs_jira_connections_jira_connection_id",
                        column: x => x.jira_connection_id,
                        principalSchema: "public",
                        principalTable: "jira_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_issue_analysis_jobs_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "public",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_issue_analysis_jobs_jira_connection_id",
                schema: "public",
                table: "issue_analysis_jobs",
                column: "jira_connection_id");

            migrationBuilder.CreateIndex(
                name: "ix_issue_analysis_jobs_tenant_id_dedupe_key",
                schema: "public",
                table: "issue_analysis_jobs",
                columns: new[] { "tenant_id", "dedupe_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_issue_analysis_jobs_tenant_id_status",
                schema: "public",
                table: "issue_analysis_jobs",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_jira_connections_tenant_id",
                schema: "public",
                table: "jira_connections",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_jira_connections_webhook_secret",
                schema: "public",
                table: "jira_connections",
                column: "webhook_secret",
                unique: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE public.jira_connections ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.jira_connections FORCE ROW LEVEL SECURITY;
                CREATE POLICY jira_connections_tenant_ctx ON public.jira_connections
                    FOR ALL
                    TO PUBLIC
                    USING (tenant_id = NULLIF(trim(current_setting('app.current_tenant_id', true)), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(trim(current_setting('app.current_tenant_id', true)), '')::uuid);

                ALTER TABLE public.issue_analysis_jobs ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.issue_analysis_jobs FORCE ROW LEVEL SECURITY;
                CREATE POLICY issue_analysis_jobs_tenant_ctx ON public.issue_analysis_jobs
                    FOR ALL
                    TO PUBLIC
                    USING (tenant_id = NULLIF(trim(current_setting('app.current_tenant_id', true)), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(trim(current_setting('app.current_tenant_id', true)), '')::uuid);

                CREATE OR REPLACE FUNCTION public.fn_resolve_jira_connection_by_webhook_secret(p_webhook_secret text)
                RETURNS TABLE (
                    id uuid,
                    tenant_id uuid,
                    site_base_url text,
                    email text,
                    api_token_protected text,
                    webhook_secret text,
                    project_keys_csv text,
                    enabled boolean,
                    tenant_status text
                )
                LANGUAGE sql
                STABLE
                SECURITY DEFINER
                SET search_path = public
                AS $func$
                    SELECT
                        c.id,
                        c.tenant_id,
                        c.site_base_url::text,
                        c.email::text,
                        c.api_token_protected,
                        c.webhook_secret::text,
                        c.project_keys_csv::text,
                        c.enabled,
                        t.status::text AS tenant_status
                    FROM public.jira_connections AS c
                    INNER JOIN public.tenants AS t ON t.id = c.tenant_id
                    WHERE c.webhook_secret = p_webhook_secret
                    LIMIT 1;
                $func$;

                COMMENT ON FUNCTION public.fn_resolve_jira_connection_by_webhook_secret(text) IS
                    'Resolves a Jira connection by webhook secret (bypasses FORCE RLS). Returns tenant status for caller gating.';

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

                COMMENT ON FUNCTION public.fn_list_stale_pending_jira_jobs(timestamptz, int) IS
                    'Lists stale PendingPublish Jira issue-analysis jobs across tenants (bypasses FORCE RLS).';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP FUNCTION IF EXISTS public.fn_list_stale_pending_jira_jobs(timestamptz, int);
                DROP FUNCTION IF EXISTS public.fn_resolve_jira_connection_by_webhook_secret(text);

                DROP POLICY IF EXISTS issue_analysis_jobs_tenant_ctx ON public.issue_analysis_jobs;
                ALTER TABLE public.issue_analysis_jobs NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE public.issue_analysis_jobs DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS jira_connections_tenant_ctx ON public.jira_connections;
                ALTER TABLE public.jira_connections NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE public.jira_connections DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "data_protection_keys",
                schema: "public");

            migrationBuilder.DropTable(
                name: "issue_analysis_jobs",
                schema: "public");

            migrationBuilder.DropTable(
                name: "jira_connections",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "issues_analyzed",
                schema: "public",
                table: "tenant_metrics");
        }
    }
}
