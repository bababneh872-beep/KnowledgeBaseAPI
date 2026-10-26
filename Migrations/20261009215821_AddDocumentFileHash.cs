using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBaseAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentFileHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileHash",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileHash",
                table: "Documents");
        }
    }
}
