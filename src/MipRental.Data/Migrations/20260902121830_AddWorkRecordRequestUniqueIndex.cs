using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MipRental.Data.Migrations
{
    /// <summary>
    /// Adım 12 (A2) — bir talepten YALNIZCA BİR çalışma kaydı türetilebilir.
    /// Çift türetme = çift faturalama, bu yüzden garanti veritabanında.
    ///
    /// Mevcut IX_WorkRecords_RequestId (FK için otomatik açılan, tekrarlanabilir
    /// index) yerini filtreli UNIQUE index'e bırakır:
    ///   RequestId IS NOT NULL — talepsiz kayıt (doğrudan giriş) hâlâ mümkün
    ///   RevisionOfId IS NULL  — revizyon selefinin RequestId'sini taşır ve aynı
    ///                           işin yeni versiyonudur, ikinci türetme değil
    ///
    /// Index yine RequestId ile başladığı için FK aramaları da bundan yararlanır;
    /// eski index'i düşürmek sorgu tarafında kayıp yaratmaz.
    /// </summary>
    public partial class AddWorkRecordRequestUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkRecords_RequestId",
                table: "WorkRecords");

            migrationBuilder.CreateIndex(
                name: "UQ_WorkRecords_Request",
                table: "WorkRecords",
                column: "RequestId",
                unique: true,
                filter: "[RequestId] IS NOT NULL AND [RevisionOfId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_WorkRecords_Request",
                table: "WorkRecords");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRecords_RequestId",
                table: "WorkRecords",
                column: "RequestId");
        }
    }
}
