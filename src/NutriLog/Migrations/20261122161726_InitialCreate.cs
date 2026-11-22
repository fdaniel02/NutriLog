using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NutriLog.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Foods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(50)", unicode: false, maxLength: 50, nullable: true),
                    Barcode = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: true),
                    Basis = table.Column<string>(type: "character varying(15)", unicode: false, maxLength: 15, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Foods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Nutrients",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Group = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nutrients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Recipes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Servings = table.Column<double>(type: "double precision", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recipes", x => x.Id);
                    table.CheckConstraint("CK_Recipes_Servings", "\"Servings\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "Portions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FoodId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Grams = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Portions", x => x.Id);
                    table.UniqueConstraint("AK_Portions_Id_FoodId", x => new { x.Id, x.FoodId });
                    table.CheckConstraint("CK_Portions_Grams", "\"Grams\" > 0");
                    table.ForeignKey(
                        name: "FK_Portions_Foods_FoodId",
                        column: x => x.FoodId,
                        principalTable: "Foods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FoodNutrients",
                columns: table => new
                {
                    FoodId = table.Column<int>(type: "integer", nullable: false),
                    NutrientId = table.Column<string>(type: "character varying(30)", nullable: false),
                    Amount = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoodNutrients", x => new { x.FoodId, x.NutrientId });
                    table.CheckConstraint("CK_FoodNutrients_Amount", "\"Amount\" >= 0");
                    table.ForeignKey(
                        name: "FK_FoodNutrients_Foods_FoodId",
                        column: x => x.FoodId,
                        principalTable: "Foods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FoodNutrients_Nutrients_NutrientId",
                        column: x => x.NutrientId,
                        principalTable: "Nutrients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecipeIngredients",
                columns: table => new
                {
                    RecipeId = table.Column<int>(type: "integer", nullable: false),
                    FoodId = table.Column<int>(type: "integer", nullable: false),
                    Grams = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeIngredients", x => new { x.RecipeId, x.FoodId });
                    table.CheckConstraint("CK_RecipeIngredients_Grams", "\"Grams\" > 0");
                    table.ForeignKey(
                        name: "FK_RecipeIngredients_Foods_FoodId",
                        column: x => x.FoodId,
                        principalTable: "Foods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecipeIngredients_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiaryEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Meal = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
                    FoodId = table.Column<int>(type: "integer", nullable: true),
                    RecipeId = table.Column<int>(type: "integer", nullable: true),
                    Quantity = table.Column<double>(type: "double precision", nullable: false),
                    QuantityUnit = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
                    PortionId = table.Column<int>(type: "integer", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiaryEntries", x => x.Id);
                    table.CheckConstraint("CK_DiaryEntries_FoodOrRecipe", "(\"FoodId\" IS NULL) <> (\"RecipeId\" IS NULL)");
                    table.CheckConstraint("CK_DiaryEntries_Portion", "(\"QuantityUnit\" = 'Portion') = (\"PortionId\" IS NOT NULL)");
                    table.CheckConstraint("CK_DiaryEntries_Quantity", "\"Quantity\" > 0");
                    table.CheckConstraint("CK_DiaryEntries_Servings", "(\"QuantityUnit\" = 'Servings') = (\"RecipeId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_DiaryEntries_Foods_FoodId",
                        column: x => x.FoodId,
                        principalTable: "Foods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiaryEntries_Portions_PortionId_FoodId",
                        columns: x => new { x.PortionId, x.FoodId },
                        principalTable: "Portions",
                        principalColumns: new[] { "Id", "FoodId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiaryEntries_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Nutrients",
                columns: new[] { "Id", "Group", "Name", "SortOrder", "Unit" },
                values: new object[,]
                {
                    { "alcohol", "Macros", "Alcohol", 7, "g" },
                    { "calcium", "Minerals", "Calcium", 26, "mg" },
                    { "carbohydrate", "Macros", "Carbohydrate", 4, "g" },
                    { "cholesterol", "FattyAcids", "Cholesterol", 12, "mg" },
                    { "copper", "Minerals", "Copper", 31, "mg" },
                    { "energy", "Energy", "Energy", 1, "kcal" },
                    { "fat", "Macros", "Fat", 3, "g" },
                    { "fibre", "Macros", "Fibre", 5, "g" },
                    { "folate", "Vitamins", "Folate", 22, "µg" },
                    { "iodine", "Minerals", "Iodine", 33, "µg" },
                    { "iron", "Minerals", "Iron", 29, "mg" },
                    { "magnesium", "Minerals", "Magnesium", 27, "mg" },
                    { "monounsaturated_fat", "FattyAcids", "Monounsaturated fat", 9, "g" },
                    { "phosphorus", "Minerals", "Phosphorus", 28, "mg" },
                    { "polyunsaturated_fat", "FattyAcids", "Polyunsaturated fat", 10, "g" },
                    { "potassium", "Minerals", "Potassium", 25, "mg" },
                    { "protein", "Macros", "Protein", 2, "g" },
                    { "saturated_fat", "FattyAcids", "Saturated fat", 8, "g" },
                    { "selenium", "Minerals", "Selenium", 32, "µg" },
                    { "sodium", "Minerals", "Sodium", 24, "mg" },
                    { "sugars", "Macros", "Sugars", 6, "g" },
                    { "trans_fat", "FattyAcids", "Trans fat", 11, "g" },
                    { "vitamin_a", "Vitamins", "Vitamin A", 13, "µg" },
                    { "vitamin_b1", "Vitamins", "Vitamin B1 (thiamin)", 17, "mg" },
                    { "vitamin_b12", "Vitamins", "Vitamin B12", 21, "µg" },
                    { "vitamin_b2", "Vitamins", "Vitamin B2 (riboflavin)", 18, "mg" },
                    { "vitamin_b3", "Vitamins", "Vitamin B3 (niacin)", 19, "mg" },
                    { "vitamin_b6", "Vitamins", "Vitamin B6", 20, "mg" },
                    { "vitamin_c", "Vitamins", "Vitamin C", 23, "mg" },
                    { "vitamin_d", "Vitamins", "Vitamin D", 14, "µg" },
                    { "vitamin_e", "Vitamins", "Vitamin E", 15, "mg" },
                    { "vitamin_k", "Vitamins", "Vitamin K", 16, "µg" },
                    { "zinc", "Minerals", "Zinc", 30, "mg" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiaryEntries_Date",
                table: "DiaryEntries",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_DiaryEntries_FoodId",
                table: "DiaryEntries",
                column: "FoodId");

            migrationBuilder.CreateIndex(
                name: "IX_DiaryEntries_PortionId_FoodId",
                table: "DiaryEntries",
                columns: new[] { "PortionId", "FoodId" });

            migrationBuilder.CreateIndex(
                name: "IX_DiaryEntries_RecipeId",
                table: "DiaryEntries",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_FoodNutrients_NutrientId",
                table: "FoodNutrients",
                column: "NutrientId");

            migrationBuilder.CreateIndex(
                name: "IX_Foods_Barcode",
                table: "Foods",
                column: "Barcode");

            migrationBuilder.CreateIndex(
                name: "IX_Foods_Source_SourceId",
                table: "Foods",
                columns: new[] { "Source", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Portions_FoodId",
                table: "Portions",
                column: "FoodId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIngredients_FoodId",
                table: "RecipeIngredients",
                column: "FoodId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "DiaryEntries");

            migrationBuilder.DropTable(
                name: "FoodNutrients");

            migrationBuilder.DropTable(
                name: "RecipeIngredients");

            migrationBuilder.DropTable(
                name: "Portions");

            migrationBuilder.DropTable(
                name: "Nutrients");

            migrationBuilder.DropTable(
                name: "Recipes");

            migrationBuilder.DropTable(
                name: "Foods");
        }
    }
}
