using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaWallet.Api.Migrations
{
    /// <inheritdoc />
    public partial class WalletCurrencyNgNCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_wallets_currency_ngn",
                table: "wallets",
                sql: "currency = 'NGN'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_wallets_currency_ngn",
                table: "wallets");
        }
    }
}
