using Careeroc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Tests;

internal static class TestDbContextFactory
{
    public static CareerocDbContext Create(string databaseName)
    {
        var options = new DbContextOptionsBuilder<CareerocDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new CareerocDbContext(options);
    }
}
