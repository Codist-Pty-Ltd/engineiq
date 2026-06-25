using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EngineIQ.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Session10_PaystackBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "billing_status",
                schema: "public",
                table: "tenants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<string>(
                name: "paystack_customer_code",
                schema: "public",
                table: "tenants",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "paystack_subscription_code",
                schema: "public",
                table: "tenants",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trial_ends_at",
                schema: "public",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "public",
                table: "tenants",
                keyColumn: "id",
                keyValue: new Guid("f1111111-1111-1111-1111-111111111111"),
                columns: new[] { "billing_status", "paystack_customer_code", "paystack_subscription_code", "trial_ends_at" },
                values: new object[] { "Internal", null, null, null });

            migrationBuilder.Sql(
                """
                UPDATE public.tenants
                SET billing_status = 'Internal'
                WHERE lower(contact_email) IN (
                    'hello@mybillable.co.za',
                    'hello@therecord.co.za',
                    'hello@skillbay.co.za',
                    'technical@codist.co.za',
                    'hello@codist.co.za'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "billing_status",
                schema: "public",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "paystack_customer_code",
                schema: "public",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "paystack_subscription_code",
                schema: "public",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "trial_ends_at",
                schema: "public",
                table: "tenants");
        }
    }
}
