using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CashFlowEntryService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppendOnlyTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Append-only at the database (INV-001): a BEFORE trigger on every
            // UPDATE/DELETE, regardless of who or what issues the statement. TRUNCATE is
            // deliberately NOT blocked — Postgres never fires row-level triggers for it — but
            // nothing in this deliverable issues TRUNCATE against this table.
            migrationBuilder.Sql("""
                CREATE FUNCTION fn_cash_flow_entries_append_only() RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION 'cash_flow_entries is append-only: % is not allowed (INV-001)', TG_OP;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_cash_flow_entries_append_only
                BEFORE UPDATE OR DELETE ON cash_flow_entries
                FOR EACH ROW EXECUTE FUNCTION fn_cash_flow_entries_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_cash_flow_entries_append_only ON cash_flow_entries;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS fn_cash_flow_entries_append_only();");
        }
    }
}
