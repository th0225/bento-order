using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace bento_order.Migrations
{
    /// <inheritdoc />
    public partial class AddBentoItemToOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BentoItemId",
                table: "Orders");

            migrationBuilder.AddColumn<string>(
                name: "AddBentoName",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddBentoOption",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BentoName",
                table: "Orders",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BentoOption",
                table: "Orders",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId_OrderDate",
                table: "Orders",
                columns: new[] { "UserId", "OrderDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_UserId_OrderDate",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AddBentoName",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AddBentoOption",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BentoName",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BentoOption",
                table: "Orders");

            migrationBuilder.AddColumn<int>(
                name: "BentoItemId",
                table: "Orders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
