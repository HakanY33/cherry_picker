using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MipRental.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkRecordMobilizationFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MobilizationFee",
                table: "WorkRecords",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MobilizationFee",
                table: "WorkRecords");
        }
    }
}
