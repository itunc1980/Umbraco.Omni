using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Umbraco.Cms.Persistence.EFCore.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "umbraco_open_iddict_applications",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    application_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    client_secret = table.Column<string>(type: "text", nullable: true),
                    client_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    consent_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    json_web_key_set = table.Column<string>(type: "text", nullable: true),
                    permissions = table.Column<string>(type: "text", nullable: true),
                    post_logout_redirect_uris = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redirect_uris = table.Column<string>(type: "text", nullable: true),
                    requirements = table.Column<string>(type: "text", nullable: true),
                    settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_umbraco_open_iddict_applications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "umbraco_open_iddict_scopes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    descriptions = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    resources = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_umbraco_open_iddict_scopes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "umbraco_user",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_disabled = table.Column<bool>(type: "boolean", nullable: false),
                    key = table.Column<Guid>(type: "uuid", nullable: false),
                    user_no_console = table.Column<bool>(type: "boolean", nullable: false),
                    user_name = table.Column<string>(type: "text", nullable: false),
                    user_login = table.Column<string>(type: "text", nullable: true),
                    user_password = table.Column<string>(type: "text", nullable: true),
                    password_config = table.Column<string>(type: "text", nullable: true),
                    user_email = table.Column<string>(type: "text", nullable: false),
                    user_language = table.Column<string>(type: "text", nullable: true),
                    security_stamp_token = table.Column<string>(type: "text", nullable: true),
                    failed_login_attempts = table.Column<int>(type: "integer", nullable: true),
                    last_lockout_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_password_change_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_login_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    email_confirmed_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    invited_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    create_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    update_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    avatar = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_umbraco_user", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "umbraco_user_group",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    key = table.Column<Guid>(type: "uuid", nullable: false),
                    user_group_alias = table.Column<string>(type: "text", nullable: true),
                    user_group_name = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    user_group_default_permissions = table.Column<string>(type: "text", nullable: true),
                    create_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    update_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    icon = table.Column<string>(type: "text", nullable: true),
                    has_access_to_all_languages = table.Column<bool>(type: "boolean", nullable: false),
                    start_content_id = table.Column<int>(type: "integer", nullable: true),
                    start_media_id = table.Column<int>(type: "integer", nullable: true),
                    start_element_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_umbraco_user_group", x => x.id);
                    table.UniqueConstraint("ak_umbraco_user_group_key", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "umbraco_open_iddict_authorizations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    application_id = table.Column<string>(type: "text", nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    creation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    scopes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_umbraco_open_iddict_authorizations", x => x.id);
                    table.ForeignKey(
                        name: "fk_umbraco_open_iddict_authorizations_umbraco_open_iddict_appl~",
                        column: x => x.application_id,
                        principalTable: "umbraco_open_iddict_applications",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "umbraco_user_start_node",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    start_node = table.Column<int>(type: "integer", nullable: false),
                    start_node_type = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_umbraco_user_start_node", x => x.id);
                    table.ForeignKey(
                        name: "fk_umbraco_user_start_node_umbraco_user_user_id",
                        column: x => x.user_id,
                        principalTable: "umbraco_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "umbraco_user_group2_app",
                columns: table => new
                {
                    user_group_id = table.Column<int>(type: "integer", nullable: false),
                    app = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_umbraco_user_group2_app", x => new { x.user_group_id, x.app });
                    table.ForeignKey(
                        name: "fk_umbraco_user_group2_app_umbraco_user_group_user_group_id",
                        column: x => x.user_group_id,
                        principalTable: "umbraco_user_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "umbraco_user_group2_granular_permission",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_group_key = table.Column<Guid>(type: "uuid", nullable: false),
                    unique_id = table.Column<Guid>(type: "uuid", nullable: true),
                    permission = table.Column<string>(type: "text", nullable: false),
                    context = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_umbraco_user_group2_granular_permission", x => x.id);
                    table.ForeignKey(
                        name: "fk_umbraco_user_group2_granular_permission_umbraco_user_group_us~",
                        column: x => x.user_group_key,
                        principalTable: "umbraco_user_group",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "umbraco_user_group2_language",
                columns: table => new
                {
                    user_group_id = table.Column<int>(type: "integer", nullable: false),
                    language_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_umbraco_user_group2_language", x => new { x.user_group_id, x.language_id });
                    table.ForeignKey(
                        name: "fk_umbraco_user_group2_language_umbraco_user_group_user_group_id",
                        column: x => x.user_group_id,
                        principalTable: "umbraco_user_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "umbraco_user_group2_permission",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_group_key = table.Column<Guid>(type: "uuid", nullable: false),
                    permission = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_umbraco_user_group2_permission", x => x.id);
                    table.ForeignKey(
                        name: "fk_umbraco_user_group2_permission_umbraco_user_group_user_group_~",
                        column: x => x.user_group_key,
                        principalTable: "umbraco_user_group",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "umbraco_user2_user_group",
                columns: table => new
                {
                    user_group_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_umbraco_user2_user_group", x => new { x.user_group_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_umbraco_user2_user_group_umbraco_user_group_user_group_id",
                        column: x => x.user_group_id,
                        principalTable: "umbraco_user_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_umbraco_user2_user_group_umbraco_user_user_id",
                        column: x => x.user_id,
                        principalTable: "umbraco_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "umbraco_open_iddict_tokens",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    application_id = table.Column<string>(type: "text", nullable: true),
                    authorization_id = table.Column<string>(type: "text", nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    creation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expiration_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    payload = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redemption_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reference_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_umbraco_open_iddict_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_umbraco_open_iddict_tokens_umbraco_open_iddict_applications~",
                        column: x => x.application_id,
                        principalTable: "umbraco_open_iddict_applications",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_umbraco_open_iddict_tokens_umbraco_open_iddict_authorizatio~",
                        column: x => x.authorization_id,
                        principalTable: "umbraco_open_iddict_authorizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_umbraco_open_iddict_applications_client_id",
                table: "umbraco_open_iddict_applications",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_umbraco_open_iddict_authorizations_application_id_status_su~",
                table: "umbraco_open_iddict_authorizations",
                columns: new[] { "application_id", "status", "subject", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_umbraco_open_iddict_scopes_name",
                table: "umbraco_open_iddict_scopes",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_umbraco_open_iddict_tokens_application_id_status_subject_ty~",
                table: "umbraco_open_iddict_tokens",
                columns: new[] { "application_id", "status", "subject", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_umbraco_open_iddict_tokens_authorization_id",
                table: "umbraco_open_iddict_tokens",
                column: "authorization_id");

            migrationBuilder.CreateIndex(
                name: "ix_umbraco_open_iddict_tokens_reference_id",
                table: "umbraco_open_iddict_tokens",
                column: "reference_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_umbraco_user_group2_granular_permission_user_group_key",
                table: "umbraco_user_group2_granular_permission",
                column: "user_group_key");

            migrationBuilder.CreateIndex(
                name: "ix_umbraco_user_group2_permission_user_group_key",
                table: "umbraco_user_group2_permission",
                column: "user_group_key");

            migrationBuilder.CreateIndex(
                name: "ix_umbraco_user_start_node_user_id",
                table: "umbraco_user_start_node",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_umbraco_user2_user_group_user_id",
                table: "umbraco_user2_user_group",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "umbraco_open_iddict_scopes");

            migrationBuilder.DropTable(
                name: "umbraco_open_iddict_tokens");

            migrationBuilder.DropTable(
                name: "umbraco_user_group2_app");

            migrationBuilder.DropTable(
                name: "umbraco_user_group2_granular_permission");

            migrationBuilder.DropTable(
                name: "umbraco_user_group2_language");

            migrationBuilder.DropTable(
                name: "umbraco_user_group2_permission");

            migrationBuilder.DropTable(
                name: "umbraco_user_start_node");

            migrationBuilder.DropTable(
                name: "umbraco_user2_user_group");

            migrationBuilder.DropTable(
                name: "umbraco_open_iddict_authorizations");

            migrationBuilder.DropTable(
                name: "umbraco_user_group");

            migrationBuilder.DropTable(
                name: "umbraco_user");

            migrationBuilder.DropTable(
                name: "umbraco_open_iddict_applications");
        }
    }
}
