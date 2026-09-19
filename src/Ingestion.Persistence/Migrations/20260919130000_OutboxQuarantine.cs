using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ingestion.Persistence.Migrations;

[DbContext(typeof(IngestionDbContext))]
[Migration("20260919130000_OutboxQuarantine")]
public partial class OutboxQuarantine : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "quarantined_at",
            table: "outbox_messages",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "quarantine_reason",
            table: "outbox_messages",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "quarantined_at", table: "outbox_messages");
        migrationBuilder.DropColumn(name: "quarantine_reason", table: "outbox_messages");
    }
}
