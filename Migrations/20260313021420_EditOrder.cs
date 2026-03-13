using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace bento_order.Migrations
{
    /// <inheritdoc />
    public partial class EditOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RiceOption",
                table: "Orders");

            migrationBuilder.AddColumn<int>(
                name: "TotalOrder",
                table: "MonthlyMenus",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Options",
                table: "BentoItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "BentoItems",
                keyColumn: "Id",
                keyValue: 1,
                column: "Options",
                value: "");

            migrationBuilder.UpdateData(
                table: "BentoItems",
                keyColumn: "Id",
                keyValue: 2,
                column: "Options",
                value: "");

            migrationBuilder.UpdateData(
                table: "BentoItems",
                keyColumn: "Id",
                keyValue: 3,
                column: "Options",
                value: "");

            migrationBuilder.UpdateData(
                table: "BentoItems",
                keyColumn: "Id",
                keyValue: 4,
                column: "Options",
                value: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalOrder",
                table: "MonthlyMenus");

            migrationBuilder.DropColumn(
                name: "Options",
                table: "BentoItems");

            migrationBuilder.AddColumn<string>(
                name: "RiceOption",
                table: "Orders",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
