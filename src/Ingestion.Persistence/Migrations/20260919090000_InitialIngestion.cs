using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ingestion.Persistence.Migrations;

[DbContext(typeof(IngestionDbContext))]
[Migration("20260919090000_InitialIngestion")]
public partial class InitialIngestion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "received_values",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                value = table.Column<decimal>(type: "numeric", nullable: false),
                received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_received_values", x => x.Id));

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                value_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                payload = table.Column<string>(type: "text", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.Id);
                table.ForeignKey(
                    name: "FK_outbox_messages_received_values_value_id",
                    column: x => x.value_id,
                    principalTable: "received_values",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("ux_received_values_idempotency_key",
            "received_values", "idempotency_key", unique: true);
        migrationBuilder.CreateIndex("ux_outbox_messages_value_id",
            "outbox_messages", "value_id", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("outbox_messages");
        migrationBuilder.DropTable("received_values");
    }
}
