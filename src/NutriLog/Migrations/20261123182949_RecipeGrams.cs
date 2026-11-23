using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriLog.Migrations
{
    /// <inheritdoc />
    public partial class RecipeGrams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_DiaryEntries_Servings",
                table: "DiaryEntries");

            migrationBuilder.AddColumn<double>(
                name: "ServingGrams",
                table: "DiaryEntries",
                type: "double precision",
                nullable: true);

            // Entries logged before don't know the recipe's weight then; the recipe's weight now is the closest there is.
            migrationBuilder.Sql("""
                UPDATE "DiaryEntries" AS e
                SET "ServingGrams" = (SELECT sum(i."Grams") FROM "RecipeIngredients" AS i WHERE i."RecipeId" = r."Id") / r."Servings"
                FROM "Recipes" AS r
                WHERE r."Id" = e."RecipeId"
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_DiaryEntries_RecipeUnit",
                table: "DiaryEntries",
                sql: "\"RecipeId\" IS NULL OR \"QuantityUnit\" IN ('Servings', 'Grams')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DiaryEntries_ServingGrams",
                table: "DiaryEntries",
                sql: "(\"ServingGrams\" IS NOT NULL) = (\"RecipeId\" IS NOT NULL) AND \"ServingGrams\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DiaryEntries_Servings",
                table: "DiaryEntries",
                sql: "\"QuantityUnit\" <> 'Servings' OR \"RecipeId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_DiaryEntries_RecipeUnit",
                table: "DiaryEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DiaryEntries_ServingGrams",
                table: "DiaryEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DiaryEntries_Servings",
                table: "DiaryEntries");

            migrationBuilder.Sql("""
                UPDATE "DiaryEntries"
                SET "Quantity" = "Quantity" / "ServingGrams", "QuantityUnit" = 'Servings'
                WHERE "RecipeId" IS NOT NULL AND "QuantityUnit" = 'Grams'
                """);

            migrationBuilder.DropColumn(
                name: "ServingGrams",
                table: "DiaryEntries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DiaryEntries_Servings",
                table: "DiaryEntries",
                sql: "(\"QuantityUnit\" = 'Servings') = (\"RecipeId\" IS NOT NULL)");
        }
    }
}
