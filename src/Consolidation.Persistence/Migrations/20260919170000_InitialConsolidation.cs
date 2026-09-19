using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Consolidation.Persistence.Migrations;
[DbContext(typeof(ConsolidationDbContext))]
[Migration("20260919170000_InitialConsolidation")]
public sealed class InitialConsolidation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE inbox_messages (message_id uuid PRIMARY KEY, processed_at timestamp with time zone NOT NULL);
        CREATE TABLE consolidated_totals (
            id integer PRIMARY KEY CONSTRAINT ck_consolidated_totals_singleton CHECK (id = 1),
            count bigint NOT NULL, sum numeric NOT NULL, last_updated_at timestamp with time zone NOT NULL);
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("consolidated_totals");
        migrationBuilder.DropTable("inbox_messages");
    }
}
