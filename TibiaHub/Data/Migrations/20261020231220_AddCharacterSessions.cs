using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TibiaHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScannedCharacters",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    World = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScannedCharacters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorldObservations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    World = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourceAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OnlineCount = table.Column<int>(type: "integer", nullable: false),
                    Exclusion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorldObservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OnlineSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    LoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LoginAfter = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LoginReliable = table.Column<bool>(type: "boolean", nullable: false),
                    logout_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LogoutAfter = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LogoutReliable = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnlineSessions_ScannedCharacters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "ScannedCharacters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnlineSessions_CharacterId",
                table: "OnlineSessions",
                column: "CharacterId",
                unique: true,
                filter: "logout_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineSessions_CharacterId_LoginAt",
                table: "OnlineSessions",
                columns: new[] { "CharacterId", "LoginAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OnlineSessions_LoginAt_CharacterId",
                table: "OnlineSessions",
                columns: new[] { "LoginAt", "CharacterId" });

            migrationBuilder.CreateIndex(
                name: "IX_OnlineSessions_logout_at",
                table: "OnlineSessions",
                column: "logout_at");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedCharacters_World_NormalizedName",
                table: "ScannedCharacters",
                columns: new[] { "World", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorldObservations_FetchedAt",
                table: "WorldObservations",
                column: "FetchedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorldObservations_World_FetchedAt",
                table: "WorldObservations",
                columns: new[] { "World", "FetchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorldObservations_World_SourceAt",
                table: "WorldObservations",
                columns: new[] { "World", "SourceAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnlineSessions");

            migrationBuilder.DropTable(
                name: "WorldObservations");

            migrationBuilder.DropTable(
                name: "ScannedCharacters");
        }
    }
}
