using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Splits every piece of dealer-authored, customer-facing text into an Arabic column and an
    /// English one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// **Hand-written, on purpose.** The scaffolder offered to RENAME each column — `description` to
    /// `description_en` and so on — which is one line shorter and declares, silently and
    /// irreversibly, that every word every office has written so far is English. In Jordan that is
    /// the guess most likely to be wrong.
    /// </para>
    /// <para>
    /// **What this does instead.** It adds the new columns, copies each existing value VERBATIM into
    /// whichever side its script says it belongs to, and leaves the original column exactly as it
    /// was. Nothing is translated, nothing is rewritten, and nothing is deleted. After this runs the
    /// legacy columns are a retained legacy source for migration and audit only — customer and
    /// dealer reads both use the new bilingual columns, and nothing reads the old ones at all.
    /// </para>
    /// <para>
    /// **The classification is a guess and is treated as one.** It counts Arabic-block characters
    /// against Latin letters; more Arabic wins, and a tie or a value with no letters at all goes to
    /// English, which is `Language.Default`. The presentation-form blocks are included because text
    /// pasted from a PDF or an old Word document arrives as those and renders as ordinary Arabic —
    /// counting only the basic block would file a wholly Arabic paragraph as English. Text that is
    /// genuinely both ("ممنوع التدخين. No smoking.") lands on one side and is shown to BOTH audiences
    /// by the fallback, so nothing is lost either way.
    /// </para>
    /// <para>
    /// **Why a wrong guess is cheap.** The legacy column still holds the original, byte for byte, so
    /// any misfiling can be undone from the database. The dealer can also simply move the text
    /// between two boxes in their own console. And because the reader falls back, a misfiled value
    /// still reaches every customer in the meantime — the only cost is which box the owner finds it
    /// in.
    /// </para>
    /// <para>
    /// **It proves itself before it commits.** The guard at the end refuses the whole migration
    /// unless, for every field, each copied value is identical to its source and landed on exactly
    /// one side. A regex that silently matched nothing would put everything in English and pass that
    /// check, so the dev database is also seeded with pure-Arabic, pure-English, mixed and
    /// presentation-form rows and the result is read by eye before this is applied anywhere real.
    /// </para>
    /// <para>
    /// **Deploy is stop, migrate, start.** During any overlap an old binary still writes the legacy
    /// column, and those writes are invisible to the new build. `Down` drops only the new columns, so
    /// a rollback loses edits made through the bilingual editor — acceptable pre-launch, and the
    /// reason the legacy DROP is a separate, later, hand-written migration (pre-launch item 131).
    /// </para>
    /// </remarks>
    public partial class BilingualDealerContent : Migration
    {
        /// <summary>Every field that becomes two, as (table, legacy column).</summary>
        private static readonly (string Table, string Column)[] Fields =
        [
            ("dealers", "description"),
            ("dealers", "rental_conditions"),
            ("dealers", "insurance_summary"),
            ("dealers", "pickup_instructions"),
            ("dealers", "delivery_notes"),
            ("dealers", "customer_notes"),
            ("vehicles", "description"),
        ];

        private const int MaxLength = 2000;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            foreach (var (table, column) in Fields)
            {
                migrationBuilder.AddColumn<string>(
                    name: $"{column}_ar", table: table, type: $"character varying({MaxLength})",
                    maxLength: MaxLength, nullable: true);
                migrationBuilder.AddColumn<string>(
                    name: $"{column}_en", table: table, type: $"character varying({MaxLength})",
                    maxLength: MaxLength, nullable: true);
            }

            // One spelling of the rule, applied seven times. A `pg_temp` function dies with the
            // session, so nothing of this is left behind in the schema.
            //
            // `length()` counts CHARACTERS and the columns are `character varying(n)`, which is also
            // a character limit, so Arabic is not penalised for being two bytes.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION pg_temp.khadra_mostly_arabic(t text) RETURNS boolean
                LANGUAGE sql IMMUTABLE AS $fn$
                  SELECT length(regexp_replace(t, '[^؀-ۿݐ-ݿࢠ-ࣿﭐ-﷿ﹰ-﻿]', '', 'g'))
                       > length(regexp_replace(t, '[^A-Za-z]', '', 'g'))
                $fn$;
                """);

            // The detector proves it works before it is trusted with anybody's text.
            //
            // This is not belt-and-braces. The Arabic ranges above are LITERAL characters, and they
            // got that way by an editor turning `؀` escapes into the characters themselves — so
            // any tool that re-encodes this file could equally turn them into something that matches
            // nothing. A regex that matches nothing files every value as English and sails straight
            // through the verbatim guard below, because "all English" is a perfectly self-consistent
            // answer. These three lines are what make that impossible to miss.
            migrationBuilder.Sql(
                """
                DO $selftest$
                BEGIN
                  IF NOT pg_temp.khadra_mostly_arabic('ممنوع التدخين') THEN
                    RAISE EXCEPTION 'BilingualDealerContent: the script detector does not recognise Arabic. The character ranges in this migration are corrupt; every value would have been filed as English. Nothing has been committed.';
                  END IF;
                  IF pg_temp.khadra_mostly_arabic('No smoking in the vehicle') THEN
                    RAISE EXCEPTION 'BilingualDealerContent: the script detector calls English Arabic. Nothing has been committed.';
                  END IF;
                  IF NOT pg_temp.khadra_mostly_arabic('ﻣﻤﻨﻮﻉ ﺍﻟﺘﺪﺧﻴﻦ') THEN
                    RAISE EXCEPTION 'BilingualDealerContent: the script detector misses Arabic presentation forms, which is how text pasted from a PDF arrives. Nothing has been committed.';
                  END IF;
                END
                $selftest$;
                """);

            foreach (var (table, column) in Fields)
            {
                // Idempotent: a row already carrying either language is left alone, so running this
                // twice cannot overwrite an edit made between the two runs.
                migrationBuilder.Sql(
                    $"""
                    UPDATE {table} SET
                      {column}_ar = CASE WHEN pg_temp.khadra_mostly_arabic({column}) THEN {column} END,
                      {column}_en = CASE WHEN pg_temp.khadra_mostly_arabic({column}) THEN NULL ELSE {column} END
                    WHERE {column} IS NOT NULL
                      AND {column}_ar IS NULL
                      AND {column}_en IS NULL;
                    """);

                // Verbatim, and on exactly one side. Anything else aborts the migration rather than
                // leaving a page half-copied — the same shape as `RefuseUncarryableRows` in the
                // reserve-now-pay-after-approval migration.
                migrationBuilder.Sql(
                    $"""
                    DO $guard$
                    DECLARE wrong integer;
                    BEGIN
                      SELECT count(*) INTO wrong FROM {table}
                       WHERE {column} IS NOT NULL
                         AND (coalesce({column}_ar, {column}_en) IS DISTINCT FROM {column}
                              OR ({column}_ar IS NOT NULL AND {column}_en IS NOT NULL));
                      IF wrong > 0 THEN
                        RAISE EXCEPTION
                          'BilingualDealerContent: % rows of {table}.{column} were not copied verbatim to exactly one language. Nothing has been committed.', wrong;
                      END IF;
                    END
                    $guard$;
                    """);

                // So that `\d+` explains the leftover to whoever finds it.
                //
                // Worded carefully: this column is NOT the source of truth for anything after this
                // migration. Nothing reads it — not a customer, not a dealer, not EF. It is kept so
                // the classification can be audited or redone, and it is dropped once that is no
                // longer worth keeping.
                migrationBuilder.Sql(
                    $"""
                    COMMENT ON COLUMN {table}.{column} IS
                      'Retired by BilingualDealerContent (2026-09-22). Copied verbatim to {column}_ar / {column}_en by script detection. Retained legacy source for migration/audit only; customer and dealer reads use the new bilingual columns. Unmapped by EF. Dropped by pre-launch item 131.';
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // The new columns only. The legacy ones were never written by `Up` and are not touched
            // here either — they still hold what they held before this ran, which is the whole point
            // of leaving them. Anything typed into the bilingual editor since is lost on a rollback,
            // and that is stated in the remarks above rather than papered over.
            foreach (var (table, column) in Fields)
            {
                migrationBuilder.DropColumn(name: $"{column}_ar", table: table);
                migrationBuilder.DropColumn(name: $"{column}_en", table: table);
            }
        }
    }
}
