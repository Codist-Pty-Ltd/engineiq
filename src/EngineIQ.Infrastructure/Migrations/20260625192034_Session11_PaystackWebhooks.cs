using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EngineIQ.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Session11_PaystackWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "paystack_webhook_events",
                schema: "public",
                columns: table => new
                {
                    event_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    event_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paystack_webhook_events", x => x.event_key);
                });

            migrationBuilder.UpdateData(
                schema: "public",
                table: "tenants",
                keyColumn: "id",
                keyValue: new Guid("f1111111-1111-1111-1111-111111111111"),
                column: "billing_status",
                value: "Internal");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "paystack_webhook_events",
                schema: "public");

            migrationBuilder.UpdateData(
                schema: "public",
                table: "tenants",
                keyColumn: "id",
                keyValue: new Guid("f1111111-1111-1111-1111-111111111111"),
                column: "billing_status",
                value: "");
        }
    }
}
