using Khadra.Infrastructure.Persistence;
using Khadra.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The migrations describe the model. Exactly, and with nothing left over.
/// </summary>
/// <remarks>
/// <para>
/// Nothing else in this suite would notice: the persistence tests build their schema with
/// <c>EnsureCreated</c> straight from the model, so a model change with no migration behind it passes
/// every one of them and then fails on a real database — in Development at startup, and in production
/// as a column that is not there.
/// </para>
/// <para>
/// No connection is opened. <c>HasPendingModelChanges</c> compares the model in this build against the
/// snapshot the migrations left, both of which are in the assembly.
/// </para>
/// </remarks>
public sealed class MigrationModelDriftTests
{
    [Fact]
    public void Every_model_change_has_a_migration_behind_it()
    {
        var options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseNpgsql(
                TestHostConfiguration.UnreachableConnection,
                npgsql => npgsql.MigrationsAssembly(typeof(KhadraDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;

        using var context = new KhadraDbContext(options);

        Assert.False(
            context.Database.HasPendingModelChanges(),
            "The EF model has changes no migration describes. Run: dotnet ef migrations add <Name> "
            + "--project Khadra.Infrastructure --startup-project Khadra.WebAPI --output-dir Persistence/Migrations");
    }
}
