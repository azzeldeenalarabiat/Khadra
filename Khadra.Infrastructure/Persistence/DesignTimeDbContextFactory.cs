using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Khadra.Infrastructure.Persistence;

// Lets `dotnet ef migrations add` build the model without booting the API (and without real secrets).
// Migrations are generated from the model only; the placeholder connection is never opened.
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<KhadraDbContext>
{
    public KhadraDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("KHADRA_DESIGNTIME_CONNECTION")
            ?? "Host=localhost;Database=khadra;Username=postgres;Password=placeholder";

        var options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(KhadraDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new KhadraDbContext(options);
    }
}
