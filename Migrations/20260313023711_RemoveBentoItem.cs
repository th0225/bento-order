using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace bento_order.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBentoItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_BentoItems_BentoItemId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Users_UserId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "BentoItems");

            migrationBuilder.DropIndex(
                name: "IX_Orders_BentoItemId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_UserId",
                table: "Orders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BentoItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Options = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BentoItems", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "BentoItems",
                columns: new[] { "Id", "Name", "Options" },
                values: new object[,]
                {
                    { 1, "A餐", "" },
                    { 2, "B餐", "" },
                    { 3, "素食", "" },
                    { 4, "合菜", "" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BentoItemId",
                table: "Orders",
                column: "BentoItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId",
                table: "Orders",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_BentoItems_BentoItemId",
                table: "Orders",
                column: "BentoItemId",
                principalTable: "BentoItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Users_UserId",
                table: "Orders",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
