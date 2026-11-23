using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriLog.Migrations
{
    /// <inheritdoc />
    public partial class DropMeal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Meal",
                table: "DiaryEntries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Which meal an entry was in is gone; Snack is the only one that fits any of them.
            migrationBuilder.AddColumn<string>(
                name: "Meal",
                table: "DiaryEntries",
                type: "character varying(10)",
                unicode: false,
                maxLength: 10,
                nullable: false,
                defaultValue: "Snack");
        }
    }
}
