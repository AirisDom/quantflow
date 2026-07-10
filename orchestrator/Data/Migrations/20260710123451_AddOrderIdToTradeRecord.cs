using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantFlow.Orchestrator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderIdToTradeRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrderId",
                table: "TradeRecords",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "TradeRecords");
        }
    }
}
