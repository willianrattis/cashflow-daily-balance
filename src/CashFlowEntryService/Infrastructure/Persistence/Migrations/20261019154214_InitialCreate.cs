using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CashFlowEntryService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cash_flow_entries",
                columns: table => new
                {
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    amount_in_cents = table.Column<long>(type: "bigint", nullable: false),
                    nature = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    principal_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_flow_entries", x => x.entry_id);
                    table.CheckConstraint("ck_cash_flow_entries_amount_positive", "amount_in_cents > 0");
                    table.CheckConstraint("ck_cash_flow_entries_nature_valid", "nature IN ('Credit', 'Debit')");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    outbox_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_version = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    published_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.outbox_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cash_flow_entries_business_date",
                table: "cash_flow_entries",
                columns: new[] { "business_date", "recorded_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_cash_flow_entries_idempotency_key",
                table: "cash_flow_entries",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                table: "outbox_messages",
                columns: new[] { "status", "next_attempt_at_utc" },
                filter: "status <> 'Published'");

            migrationBuilder.CreateIndex(
                name: "ux_outbox_aggregate_event",
                table: "outbox_messages",
                columns: new[] { "aggregate_id", "event_type", "event_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_outbox_event_id",
                table: "outbox_messages",
                column: "event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_flow_entries");

            migrationBuilder.DropTable(
                name: "outbox_messages");
        }
    }
}
