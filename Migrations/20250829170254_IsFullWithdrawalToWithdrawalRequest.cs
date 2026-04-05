using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShantiBotDi.Migrations
{
    /// <inheritdoc />
    public partial class IsFullWithdrawalToWithdrawalRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFullWithdrawal",
                table: "WithdrawalRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFullWithdrawal",
                table: "WithdrawalRequests");
        }
    }
}
