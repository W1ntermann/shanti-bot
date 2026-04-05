using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShantiBotDi.Migrations
{
    /// <inheritdoc />
    public partial class Has25PercentBonusToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsFullWithdrawal",
                table: "WithdrawalRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AddColumn<bool>(
                name: "Has25PercentBonus",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_CreatedAt",
                table: "WithdrawalRequests",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_Status",
                table: "WithdrawalRequests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WithdrawalRequests_CreatedAt",
                table: "WithdrawalRequests");

            migrationBuilder.DropIndex(
                name: "IX_WithdrawalRequests_Status",
                table: "WithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "Has25PercentBonus",
                table: "Users");

            migrationBuilder.AlterColumn<bool>(
                name: "IsFullWithdrawal",
                table: "WithdrawalRequests",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);
        }
    }
}
