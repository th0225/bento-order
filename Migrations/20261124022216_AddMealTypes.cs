using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace bento_order.Migrations
{
    /// <inheritdoc />
    public partial class AddMealTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MealTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealTypes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MealTypes_Name",
                table: "MealTypes",
                column: "Name",
                unique: true);

            // 原本寫死在 MealProvider 的四種餐點，搬進資料表
            migrationBuilder.Sql(
                """
                INSERT INTO "MealTypes" ("Name", "SortOrder") VALUES
                    ('A餐', 1), ('B餐', 2), ('素食', 3), ('合菜', 4);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MealTypes");
        }
    }
}
