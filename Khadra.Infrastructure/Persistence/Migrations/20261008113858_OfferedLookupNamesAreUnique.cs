using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OfferedLookupNamesAreUnique : Migration
    {
        // Pre-launch item 52. The lookup handlers refuse a name another OFFERED entry already uses
        // (`lookup.name_taken`), but a handler's read can lose a race: two administrators creating the same
        // city at once both read "free" and both insert. These partial unique indexes are the floor under
        // that read — the loser fails, and the handler turns the index's name back into `lookup.name_taken`.
        //
        // The key is `LookupEntry.ComparisonKey` in SQL: tashkeel (U+064B–U+0652) and tatweel (U+0640)
        // removed, then lower-cased. Offered rows only (`WHERE is_active`): a retired entry reserves nothing,
        // as in the handler.
        //
        // It refuses to apply while two offered entries already share a name, and never fixes data itself:
        // which of two duplicates to retire is an administrator's call, made on /cities or /car-types. The
        // check runs first and names them, so the refusal is a sentence rather than a unique violation, and
        // the transaction leaves nothing behind.
        private const string Key = "lower(regexp_replace({0}, '[\\u064B-\\u0652\\u0640]', '', 'g'))";

        private static string Duplicates(string table, string column) => $@"
    SELECT '{table}.{column} ' || string_agg({column}, ' = ') AS duplicate
    FROM {table} WHERE is_active
    GROUP BY {string.Format(System.Globalization.CultureInfo.InvariantCulture, Key, column)}
    HAVING count(*) > 1";

        private static string Index(string table, string column) =>
            $"CREATE UNIQUE INDEX ux_{table}_offered_{column} ON {table} ({string.Format(System.Globalization.CultureInfo.InvariantCulture, Key, column)}) WHERE is_active;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
DO $$
DECLARE found text;
BEGIN
    SELECT string_agg(duplicate, '; ') INTO found FROM ({Duplicates("cities", "name_en")}
    UNION ALL {Duplicates("cities", "name_ar")}
    UNION ALL {Duplicates("car_types", "name_en")}
    UNION ALL {Duplicates("car_types", "name_ar")}) duplicates;

    IF found IS NOT NULL THEN
        RAISE EXCEPTION 'Offered lookup entries share a name (%). Retire the duplicates on /cities or /car-types, then apply this migration again.', found;
    END IF;
END $$;");

            migrationBuilder.Sql(Index("cities", "name_en"));
            migrationBuilder.Sql(Index("cities", "name_ar"));
            migrationBuilder.Sql(Index("car_types", "name_en"));
            migrationBuilder.Sql(Index("car_types", "name_ar"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_car_types_offered_name_ar;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_car_types_offered_name_en;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_cities_offered_name_ar;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_cities_offered_name_en;");
        }
    }
}
