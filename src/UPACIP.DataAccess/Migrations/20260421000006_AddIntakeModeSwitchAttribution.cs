using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <summary>
    /// Adds intake mode-switch source attribution, conflict history, and switch-event audit
    /// columns to the <c>intake_data</c> table (US_029, AC-3, AC-4, EC-1, EC-2).
    ///
    /// Schema additions:
    ///   - <c>intake_attribution</c>   JSONB  NULL — per-field source provenance, conflict notes,
    ///                                               and ordered mode-switch event log.
    ///
    /// JSONB structure (intake_attribution):
    /// <code>
    /// {
    ///   "fieldAttributions": [
    ///     { "fieldKey": "firstName", "source": "ai",     "collectedAt": "..." }
    ///   ],
    ///   "conflictNotes": [
    ///     { "fieldKey": "phone", "winningValue": "...", "winningSource": "manual",
    ///       "replacedValue": "...", "replacedSource": "ai", "recordedAt": "..." }
    ///   ],
    ///   "modeSwitchEvents": [
    ///     { "fromMode": "ai", "toMode": "manual", "switchedAt": "...", "correlationId": null }
    ///   ]
    /// }
    /// </code>
    ///
    /// Safety / rollout considerations:
    ///   - Column is nullable: existing intake rows (AI-only or manual-only) are unaffected.
    ///     NULL means "no mode switch occurred" and the application treats NULL as no attribution.
    ///   - JSONB is schema-less; new fields can be added to the owned types without a migration.
    ///   - Down() drops the column cleanly for rollback safety.
    /// </summary>
    public partial class AddIntakeModeSwitchAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Per-field source provenance, conflict audit log, and mode-switch event history.
            // Stored as a single JSONB column — NULL until the first mode switch occurs (EC-2).
            // Three nested JSON arrays: fieldAttributions, conflictNotes, modeSwitchEvents.
            migrationBuilder.AddColumn<string>(
                name:     "intake_attribution",
                table:    "intake_data",
                type:     "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name:  "intake_attribution",
                table: "intake_data");
        }
    }
}
