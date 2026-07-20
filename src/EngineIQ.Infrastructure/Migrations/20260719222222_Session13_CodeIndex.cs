using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace EngineIQ.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Session13_CodeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Session13 requires pgvector/pgvector:pg16 — CREATE EXTENSION must run before any vector column.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.RenameColumn(
                name: "last_indexed_at",
                schema: "public",
                table: "repositories",
                newName: "indexed_at");

            migrationBuilder.AddColumn<int>(
                name: "chunks_embedded",
                schema: "public",
                table: "tenant_metrics",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "indexed_commit_sha",
                schema: "public",
                table: "repositories",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "code_chunks",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    start_line = table.Column<int>(type: "integer", nullable: false),
                    end_line = table.Column<int>(type: "integer", nullable: false),
                    content_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    symbol_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    embedding = table.Column<Vector>(type: "vector(1024)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_code_chunks", x => x.id);
                    table.ForeignKey(
                        name: "fk_code_chunks_repositories_repository_id",
                        column: x => x.repository_id,
                        principalSchema: "public",
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_code_chunks_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "public",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "repo_index_jobs",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<long>(type: "bigint", nullable: false),
                    owner = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    repo = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    head_sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    base_sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    dedupe_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    files_walked = table.Column<int>(type: "integer", nullable: false),
                    chunks_total = table.Column<int>(type: "integer", nullable: false),
                    chunks_embedded = table.Column<int>(type: "integer", nullable: false),
                    chunks_deleted = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repo_index_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_repo_index_jobs_repositories_repository_id",
                        column: x => x.repository_id,
                        principalSchema: "public",
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_repo_index_jobs_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "public",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_code_chunks_repository_id",
                schema: "public",
                table: "code_chunks",
                column: "repository_id");

            migrationBuilder.CreateIndex(
                name: "ix_code_chunks_tenant_id_repository_id",
                schema: "public",
                table: "code_chunks",
                columns: new[] { "tenant_id", "repository_id" });

            migrationBuilder.CreateIndex(
                name: "ix_code_chunks_tenant_id_repository_id_file_path_content_sha256",
                schema: "public",
                table: "code_chunks",
                columns: new[] { "tenant_id", "repository_id", "file_path", "content_sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_repo_index_jobs_repository_id",
                schema: "public",
                table: "repo_index_jobs",
                column: "repository_id");

            migrationBuilder.CreateIndex(
                name: "ix_repo_index_jobs_tenant_id_dedupe_key",
                schema: "public",
                table: "repo_index_jobs",
                columns: new[] { "tenant_id", "dedupe_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_repo_index_jobs_tenant_id_repository_id_status",
                schema: "public",
                table: "repo_index_jobs",
                columns: new[] { "tenant_id", "repository_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_repo_index_jobs_tenant_id_status",
                schema: "public",
                table: "repo_index_jobs",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.Sql(
                """
                ALTER TABLE public.code_chunks ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.code_chunks FORCE ROW LEVEL SECURITY;
                CREATE POLICY code_chunks_tenant_ctx ON public.code_chunks
                    FOR ALL
                    TO PUBLIC
                    USING (tenant_id = NULLIF(trim(current_setting('app.current_tenant_id', true)), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(trim(current_setting('app.current_tenant_id', true)), '')::uuid);

                ALTER TABLE public.repo_index_jobs ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.repo_index_jobs FORCE ROW LEVEL SECURITY;
                CREATE POLICY repo_index_jobs_tenant_ctx ON public.repo_index_jobs
                    FOR ALL
                    TO PUBLIC
                    USING (tenant_id = NULLIF(trim(current_setting('app.current_tenant_id', true)), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(trim(current_setting('app.current_tenant_id', true)), '')::uuid);

                -- HNSW index for approximate nearest-neighbour cosine search over code chunk embeddings.
                CREATE INDEX ix_code_chunks_embedding_hnsw ON public.code_chunks
                    USING hnsw (embedding vector_cosine_ops);

                CREATE OR REPLACE FUNCTION public.fn_list_stale_pending_repo_index_jobs(p_cutoff timestamptz, p_limit int)
                RETURNS TABLE (
                    tenant_id uuid,
                    job_id uuid,
                    repository_id uuid,
                    installation_id bigint,
                    owner text,
                    repo text,
                    head_sha text,
                    base_sha text
                )
                LANGUAGE sql
                STABLE
                SECURITY DEFINER
                SET search_path = public
                AS $func$
                    SELECT
                        j.tenant_id,
                        j.id AS job_id,
                        j.repository_id,
                        j.installation_id,
                        j.owner::text,
                        j.repo::text,
                        j.head_sha::text,
                        j.base_sha::text
                    FROM public.repo_index_jobs AS j
                    WHERE j.status = 'PendingPublish'
                      AND j.created_at <= p_cutoff
                    ORDER BY j.created_at
                    LIMIT GREATEST(1, LEAST(COALESCE(p_limit, 50), 200));
                $func$;

                COMMENT ON FUNCTION public.fn_list_stale_pending_repo_index_jobs(timestamptz, int) IS
                    'Lists stale PendingPublish repo code-index jobs across tenants (bypasses FORCE RLS).';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP FUNCTION IF EXISTS public.fn_list_stale_pending_repo_index_jobs(timestamptz, int);
                DROP INDEX IF EXISTS public.ix_code_chunks_embedding_hnsw;

                DROP POLICY IF EXISTS repo_index_jobs_tenant_ctx ON public.repo_index_jobs;
                ALTER TABLE public.repo_index_jobs NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE public.repo_index_jobs DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS code_chunks_tenant_ctx ON public.code_chunks;
                ALTER TABLE public.code_chunks NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE public.code_chunks DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "code_chunks",
                schema: "public");

            migrationBuilder.DropTable(
                name: "repo_index_jobs",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "chunks_embedded",
                schema: "public",
                table: "tenant_metrics");

            migrationBuilder.DropColumn(
                name: "indexed_commit_sha",
                schema: "public",
                table: "repositories");

            migrationBuilder.RenameColumn(
                name: "indexed_at",
                schema: "public",
                table: "repositories",
                newName: "last_indexed_at");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
