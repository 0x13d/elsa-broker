using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ElsaBroker.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations</c> can build the model without
/// booting the Queue or Processor host. The connection string is only used to
/// resolve SQL Server provider conventions — no database is contacted when a
/// migration is generated. Override with the <c>BROKER_DB_CONNECTION</c>
/// environment variable when scaffolding against a real instance.
/// </summary>
public sealed class BrokerDbContextFactory : IDesignTimeDbContextFactory<BrokerDbContext>
{
    public BrokerDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("BROKER_DB_CONNECTION")
            ?? "Server=localhost,1433;Database=ElsaBroker;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;";

        var options = new DbContextOptionsBuilder<BrokerDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new BrokerDbContext(options);
    }
}
