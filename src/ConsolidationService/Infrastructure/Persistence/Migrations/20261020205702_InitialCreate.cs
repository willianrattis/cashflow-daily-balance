using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConsolidationService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_consolidations",
                columns: table => new
                {
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    credits_in_cents = table.Column<long>(type: "bigint", nullable: false),
                    debits_in_cents = table.Column<long>(type: "bigint", nullable: false),
                    net_change_in_cents = table.Column<long>(type: "bigint", nullable: false),
                    last_event_occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    projection_version = table.Column<long>(type: "bigint", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_consolidations", x => x.business_date);
                    table.CheckConstraint("ck_daily_credits_non_negative", "credits_in_cents >= 0");
                    table.CheckConstraint("ck_daily_debits_non_negative", "debits_in_cents >= 0");
                    table.CheckConstraint("ck_daily_net_change_consistent", "net_change_in_cents = credits_in_cents - debits_in_cents");
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_version = table.Column<int>(type: "integer", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_messages", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "projection_metadata",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    last_applied_event_recorded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_applied_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    projection_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projection_metadata", x => x.id);
                    table.CheckConstraint("ck_projection_metadata_singleton", "id = 1");
                });

            migrationBuilder.CreateIndex(
                name: "ux_inbox_entry_id",
                table: "inbox_messages",
                column: "entry_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_consolidations");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "projection_metadata");
        }
    }
}
