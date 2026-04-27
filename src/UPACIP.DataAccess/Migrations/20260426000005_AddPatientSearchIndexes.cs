using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <summary>
    /// Enables the <c>pg_trgm</c> PostgreSQL extension and creates supporting indexes on the
    /// <c>patients</c> table to satisfy the 1-second search performance requirement (US_062 AC-1,
    /// DR-028, DR-029).
    ///
    /// Changes applied:
    ///   1. <c>CREATE EXTENSION IF NOT EXISTS pg_trgm</c> — trigram similarity functions and GIN
    ///      operator class needed for partial name matching (<c>%</c> / <c>ILIKE '%…%'</c>).
    ///   2. GIN trigram index on <c>patients.full_name</c> (<c>gin_trgm_ops</c>) — enables efficient
    ///      partial-match scans (e.g. "Joh" → "John", "Johnston") without sequential table scans.
    ///   3. B-tree index on <c>patients.phone_number</c> — exact and prefix phone lookups.
    ///   4. B-tree index on <c>patients.date_of_birth</c> — exact DOB lookups.
    ///   5. Partial B-tree index on <c>patients.full_name WHERE deleted_at IS NULL</c> —
    ///      active-patient-only name ordering with a compact, maintenance-friendly index footprint
    ///      (DR-021 soft-delete pattern).
    ///
    /// <c>Down()</c> reverses all changes in strict reverse order per DR-028.
    /// </summary>
    public partial class AddPatientSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Enable pg_trgm extension (idempotent) ───────────────────────────
            // Required before any GIN gin_trgm_ops index can be created.
            // IF NOT EXISTS guarantees the migration is re-runnable on environments
            // where the extension was already enabled manually (DR-029).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            // ── 2. GIN trigram index on patients.full_name ─────────────────────────
            // Supports ILIKE '%term%' and trigram-similarity queries with sub-linear
            // scan time on large patient tables (US_062 AC-1 — 1-second SLA).
            // Uses raw SQL because EF Core's CreateIndex() has no operator-class param.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS ix_patients_full_name_trgm " +
                "ON patients USING GIN (full_name gin_trgm_ops);");

            // ── 3. B-tree index on patients.phone_number ───────────────────────────
            // Supports exact phone-number lookups and prefix scans by staff (AC-1).
            migrationBuilder.CreateIndex(
                name:    "ix_patients_phone_number",
                table:   "patients",
                column:  "phone_number");

            // ── 4. B-tree index on patients.date_of_birth ─────────────────────────
            // Supports exact DOB queries used in patient identity verification (AC-1).
            migrationBuilder.CreateIndex(
                name:    "ix_patients_date_of_birth",
                table:   "patients",
                column:  "date_of_birth");

            // ── 5. Partial B-tree index on full_name for active patients only ──────
            // Restricts to rows WHERE deleted_at IS NULL so soft-deleted patients are
            // excluded from the index entirely, keeping its size proportional to the
            // active record set and improving name-sort scans for the search results
            // page (DR-021, US_062 AC-1).
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS ix_patients_full_name_active " +
                "ON patients (full_name) " +
                "WHERE deleted_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse order: partial index → B-tree indexes → GIN index → extension.

            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS ix_patients_full_name_active;");

            migrationBuilder.DropIndex(
                name:  "ix_patients_date_of_birth",
                table: "patients");

            migrationBuilder.DropIndex(
                name:  "ix_patients_phone_number",
                table: "patients");

            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS ix_patients_full_name_trgm;");

            // Drop the extension only if no other objects depend on it.
            // CASCADE is intentionally omitted — if another object depends on pg_trgm,
            // the migration will fail loudly rather than silently dropping dependents.
            migrationBuilder.Sql(
                "DROP EXTENSION IF EXISTS pg_trgm;");
        }
    }
}
