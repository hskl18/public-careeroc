using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Careeroc.Infrastructure.Persistence;

public sealed class CareerocDbContextFactory : IDesignTimeDbContextFactory<CareerocDbContext>
{
    public CareerocDbContext CreateDbContext(string[] args)
    {
        var connectionString = CareerocConnectionString.ResolveFromEnvironment();

        var optionsBuilder = new DbContextOptionsBuilder<CareerocDbContext>();
        optionsBuilder
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();

        return new CareerocDbContext(optionsBuilder.Options);
    }
}
