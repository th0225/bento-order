using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace bento_order.Migrations
{
    /// <inheritdoc />
    public partial class AddLockedDateNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "LockedDates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Note",
                table: "LockedDates");
        }
    }
}
