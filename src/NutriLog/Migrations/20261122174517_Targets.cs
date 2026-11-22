using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriLog.Migrations
{
    /// <inheritdoc />
    public partial class Targets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NutrientTargets",
                columns: table => new
                {
                    NutrientId = table.Column<string>(type: "character varying(30)", nullable: false),
                    Minimum = table.Column<double>(type: "double precision", nullable: true),
                    Maximum = table.Column<double>(type: "double precision", nullable: true),
                    ExtraPerRunKm = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutrientTargets", x => x.NutrientId);
                    table.CheckConstraint("CK_NutrientTargets_MinimumOrMaximum", "\"Minimum\" IS NOT NULL OR \"Maximum\" IS NOT NULL");
                    table.CheckConstraint("CK_NutrientTargets_NotNegative", "\"Minimum\" >= 0 AND \"Maximum\" >= 0 AND \"ExtraPerRunKm\" >= 0");
                    table.CheckConstraint("CK_NutrientTargets_Range", "\"Minimum\" <= \"Maximum\"");
                    table.ForeignKey(
                        name: "FK_NutrientTargets_Nutrients_NutrientId",
                        column: x => x.NutrientId,
                        principalTable: "Nutrients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NutrientTargets");
        }
    }
}
