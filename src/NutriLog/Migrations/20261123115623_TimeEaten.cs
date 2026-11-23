using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriLog.Migrations
{
    /// <inheritdoc />
    public partial class TimeEaten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "TimeEaten",
                table: "DiaryEntries",
                type: "time without time zone",
                nullable: true);

            // Existing entries count as eaten when they were logged. The zone is the TimeZone setting's value when
            // this ran; migrations can't read settings, and a later change of the setting mustn't rewrite history.
            migrationBuilder.Sql("""
                UPDATE "DiaryEntries"
                SET "TimeEaten" = date_trunc('minute', "CreatedUtc" AT TIME ZONE 'Europe/Amsterdam')::time;
                """);

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "TimeEaten",
                table: "DiaryEntries",
                type: "time without time zone",
                nullable: false,
                oldClrType: typeof(TimeOnly),
                oldType: "time without time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeEaten",
                table: "DiaryEntries");
        }
    }
}
