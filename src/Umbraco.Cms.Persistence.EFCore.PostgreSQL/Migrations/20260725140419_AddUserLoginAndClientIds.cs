using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Umbraco.Cms.Persistence.EFCore.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLoginAndClientIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "umbraco_user_login",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    logged_in_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_validated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    logged_out_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ip_address = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_umbraco_user_login", x => x.session_id);
                });

            migrationBuilder.CreateTable(
                name: "umbraco_user2_client_id",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    client_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_umbraco_user2_client_id", x => new { x.user_id, x.client_id });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "umbraco_user_login");

            migrationBuilder.DropTable(
                name: "umbraco_user2_client_id");
        }
    }
}
