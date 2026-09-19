using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ingestion.Persistence.Migrations;

[DbContext(typeof(IngestionDbContext))]
[Migration("20260919220000_OutboxTraceContext")]
public partial class OutboxTraceContext : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "traceparent", table: "outbox_messages",
            type: "character varying(256)", maxLength: 256, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "tracestate", table: "outbox_messages",
            type: "character varying(512)", maxLength: 512, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "traceparent", table: "outbox_messages");
        migrationBuilder.DropColumn(name: "tracestate", table: "outbox_messages");
    }
}
