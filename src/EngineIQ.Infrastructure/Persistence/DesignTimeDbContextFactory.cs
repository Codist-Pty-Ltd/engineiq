using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pgvector.EntityFrameworkCore;

namespace EngineIQ.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EngineIQDbContext>
{
    public EngineIQDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ENGINEIQ_PG")
            ?? "Host=localhost;Port=5432;Username=engineiq;Password=engineiq;Database=engineiq";

        var options = new DbContextOptionsBuilder<EngineIQDbContext>()
            .UseNpgsql(connectionString, b => b
                .MigrationsAssembly(typeof(EngineIQDbContext).Assembly.GetName().Name!)
                .UseVector())
            .UseSnakeCaseNamingConvention();

        return new EngineIQDbContext(options.Options);
    }
}
