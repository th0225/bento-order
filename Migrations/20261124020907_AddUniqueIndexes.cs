using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace bento_order.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonthlyMenus");

            migrationBuilder.DropIndex(
                name: "IX_Orders_UserId_OrderDate",
                table: "Orders");

            // 建立唯一索引前先整理既有資料，避免重複資料讓 migration 失敗：
            // 1. 日期統一成午夜（舊版本可能存了時間）
            // 2. 重複的資料保留 Id 最小的一筆（舊程式更新與讀取的都是這筆）
            migrationBuilder.Sql(
                """
                UPDATE "Orders"
                SET "OrderDate" = strftime('%Y-%m-%d 00:00:00', "OrderDate")
                WHERE "OrderDate" <> strftime('%Y-%m-%d 00:00:00', "OrderDate");

                DELETE FROM "Orders"
                WHERE "Id" NOT IN (
                    SELECT MIN("Id") FROM "Orders" GROUP BY "UserId", "OrderDate");

                UPDATE "LockedDates"
                SET "Date" = strftime('%Y-%m-%d 00:00:00', "Date")
                WHERE "Date" <> strftime('%Y-%m-%d 00:00:00', "Date");

                DELETE FROM "LockedDates"
                WHERE "Id" NOT IN (
                    SELECT MIN("Id") FROM "LockedDates" GROUP BY "Date");

                DELETE FROM "SystemConfig"
                WHERE "Id" NOT IN (
                    SELECT MIN("Id") FROM "SystemConfig" GROUP BY "Key");
                """);

            // 統計訊息標題改為可設定；既有系統保留原本寫死在程式中的標題
            migrationBuilder.Sql(
                """
                INSERT INTO "SystemConfig" ("Key", "Value")
                SELECT 'LineReportTitle', '新技術午餐統計'
                WHERE EXISTS (SELECT 1 FROM "Orders")
                  AND NOT EXISTS (
                      SELECT 1 FROM "SystemConfig" WHERE "Key" = 'LineReportTitle');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SystemConfig_Key",
                table: "SystemConfig",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId_OrderDate",
                table: "Orders",
                columns: new[] { "UserId", "OrderDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LockedDates_Date",
                table: "LockedDates",
                column: "Date",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SystemConfig_Key",
                table: "SystemConfig");

            migrationBuilder.DropIndex(
                name: "IX_Orders_UserId_OrderDate",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_LockedDates_Date",
                table: "LockedDates");

            migrationBuilder.CreateTable(
                name: "MonthlyMenus",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyMenus", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId_OrderDate",
                table: "Orders",
                columns: new[] { "UserId", "OrderDate" });
        }
    }
}
