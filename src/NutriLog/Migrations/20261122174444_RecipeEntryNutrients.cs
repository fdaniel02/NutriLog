using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriLog.Migrations
{
    /// <inheritdoc />
    public partial class RecipeEntryNutrients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiaryEntryNutrients",
                columns: table => new
                {
                    DiaryEntryId = table.Column<int>(type: "integer", nullable: false),
                    NutrientId = table.Column<string>(type: "character varying(30)", nullable: false),
                    PerServing = table.Column<double>(type: "double precision", nullable: false),
                    Complete = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiaryEntryNutrients", x => new { x.DiaryEntryId, x.NutrientId });
                    table.CheckConstraint("CK_DiaryEntryNutrients_PerServing", "\"PerServing\" >= 0");
                    table.ForeignKey(
                        name: "FK_DiaryEntryNutrients_DiaryEntries_DiaryEntryId",
                        column: x => x.DiaryEntryId,
                        principalTable: "DiaryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DiaryEntryNutrients_Nutrients_NutrientId",
                        column: x => x.NutrientId,
                        principalTable: "Nutrients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiaryEntryNutrients_NutrientId",
                table: "DiaryEntryNutrients",
                column: "NutrientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiaryEntryNutrients");
        }
    }
}
