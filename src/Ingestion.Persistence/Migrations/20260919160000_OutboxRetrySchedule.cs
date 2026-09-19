using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ingestion.Persistence.Migrations;

[DbContext(typeof(IngestionDbContext))]
[Migration("20260919160000_OutboxRetrySchedule")]
public partial class OutboxRetrySchedule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "publish_attempts",
            table: "outbox_messages",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "next_attempt_at",
            table: "outbox_messages",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "publish_attempts", table: "outbox_messages");
        migrationBuilder.DropColumn(name: "next_attempt_at", table: "outbox_messages");
    }
}
