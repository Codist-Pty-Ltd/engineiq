using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EngineIQ.Infrastructure.Migrations;

/// <inheritdoc />
public partial class Session14_HybridSearchAndMappings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "chunks_retrieved",
            schema: "public",
            table: "issue_analysis_jobs",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "repos_searched",
            schema: "public",
            table: "issue_analysis_jobs",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "jira_project_repo_mappings",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                jira_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                project_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_jira_project_repo_mappings", x => x.id);
                table.ForeignKey(
                    name: "fk_jira_project_repo_mappings_jira_connections_jira_connection",
                    column: x => x.jira_connection_id,
                    principalSchema: "public",
                    principalTable: "jira_connections",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_jira_project_repo_mappings_repositories_repository_id",
                    column: x => x.repository_id,
                    principalSchema: "public",
                    principalTable: "repositories",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_jira_project_repo_mappings_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "public",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_jira_project_repo_mappings_jira_connection_id_project_key_r",
            schema: "public",
            table: "jira_project_repo_mappings",
            columns: new[] { "jira_connection_id", "project_key", "repository_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_jira_project_repo_mappings_repository_id",
            schema: "public",
            table: "jira_project_repo_mappings",
            column: "repository_id");

        migrationBuilder.CreateIndex(
            name: "ix_jira_project_repo_mappings_tenant_id_jira_connection_id",
            schema: "public",
            table: "jira_project_repo_mappings",
            columns: new[] { "tenant_id", "jira_connection_id" });

        // simple config (not english) — english stemmer mangles code identifiers.
        migrationBuilder.Sql(
            """
            ALTER TABLE public.code_chunks
                ADD COLUMN IF NOT EXISTS content_tsv tsvector
                GENERATED ALWAYS AS (to_tsvector('simple', content)) STORED;

            CREATE INDEX IF NOT EXISTS ix_code_chunks_content_tsv
                ON public.code_chunks USING gin (content_tsv);

            ALTER TABLE public.jira_project_repo_mappings ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.jira_project_repo_mappings FORCE ROW LEVEL SECURITY;
            CREATE POLICY jira_project_repo_mappings_tenant_ctx ON public.jira_project_repo_mappings
                FOR ALL
                TO PUBLIC
                USING (tenant_id = NULLIF(trim(current_setting('app.current_tenant_id', true)), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(trim(current_setting('app.current_tenant_id', true)), '')::uuid);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP POLICY IF EXISTS jira_project_repo_mappings_tenant_ctx ON public.jira_project_repo_mappings;
            DROP INDEX IF EXISTS public.ix_code_chunks_content_tsv;
            ALTER TABLE public.code_chunks DROP COLUMN IF EXISTS content_tsv;
            """);

        migrationBuilder.DropTable(
            name: "jira_project_repo_mappings",
            schema: "public");

        migrationBuilder.DropColumn(
            name: "chunks_retrieved",
            schema: "public",
            table: "issue_analysis_jobs");

        migrationBuilder.DropColumn(
            name: "repos_searched",
            schema: "public",
            table: "issue_analysis_jobs");
    }
}
