using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace bento_order.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTelegramConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Telegram 通知功能已移除，刪除不再使用的 Bot Token 等設定
            migrationBuilder.Sql(
                """
                DELETE FROM "SystemConfig"
                WHERE "Key" IN ('TelegramBotToken', 'TelegramChatId');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
