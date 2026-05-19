using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EngineIQ.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Session9_PortalPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "portal_preferences_json",
                schema: "public",
                table: "tenants",
                type: "jsonb",
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "public",
                table: "tenants",
                keyColumn: "id",
                keyValue: new Guid("f1111111-1111-1111-1111-111111111111"),
                column: "portal_preferences_json",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "portal_preferences_json",
                schema: "public",
                table: "tenants");
        }
    }
}
