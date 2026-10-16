using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Magpie.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class audit_record_data : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Data",
                table: "system_audit",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Data",
                table: "system_audit");
        }
    }
}
