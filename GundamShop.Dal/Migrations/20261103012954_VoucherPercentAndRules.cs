using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GundamShop.Dal.Migrations
{
    /// <inheritdoc />
    public partial class VoucherPercentAndRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiscountPercent",
                table: "Vouchers",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Vouchers_DiscountPercent",
                table: "Vouchers",
                sql: "[DiscountPercent] IS NULL OR [DiscountPercent] BETWEEN 1 AND 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Vouchers_DiscountVnd",
                table: "Vouchers",
                sql: "[DiscountVnd] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Vouchers_MinSubtotalVnd",
                table: "Vouchers",
                sql: "[MinSubtotalVnd] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Vouchers_Period",
                table: "Vouchers",
                sql: "[StartsAtUtc] < [EndsAtUtc]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Vouchers_UsedCount",
                table: "Vouchers",
                sql: "[UsedCount] >= 0 AND [UsedCount] <= [MaxUses]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reviews_Rating",
                table: "Reviews",
                sql: "[Rating] BETWEEN 1 AND 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Vouchers_DiscountPercent",
                table: "Vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Vouchers_DiscountVnd",
                table: "Vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Vouchers_MinSubtotalVnd",
                table: "Vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Vouchers_Period",
                table: "Vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Vouchers_UsedCount",
                table: "Vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Reviews_Rating",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "Vouchers");
        }
    }
}
