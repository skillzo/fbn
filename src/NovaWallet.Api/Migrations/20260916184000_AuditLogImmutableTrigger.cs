using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaWallet.Api.Migrations
{
    /// <inheritdoc />
    public partial class AuditLogImmutableTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_audit_log_mutation()
                RETURNS trigger AS $$
                BEGIN
                  RAISE EXCEPTION 'audit_log is append-only';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER audit_log_immutable
                  BEFORE UPDATE OR DELETE ON audit_log
                  FOR EACH ROW EXECUTE FUNCTION prevent_audit_log_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS audit_log_immutable ON audit_log;
                DROP FUNCTION IF EXISTS prevent_audit_log_mutation();
                """);
        }
    }
}
