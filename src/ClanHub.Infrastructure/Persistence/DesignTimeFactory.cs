using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace ClanHub.Infrastructure.Persistence;

/// <summary>Usada só pelas ferramentas do EF (dotnet ef): não precisa de banco acessível para gerar migrations.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost;Database=ClanHub;Trusted_Connection=True;TrustServerCertificate=True";
        // Mesmo esquema do Identity usado pelo Server (versão 3 inclui passkeys).
        var services = new ServiceCollection();
        services.Configure<Microsoft.AspNetCore.Identity.IdentityOptions>(o =>
        {
            o.Stores.SchemaVersion = Microsoft.AspNetCore.Identity.IdentitySchemaVersions.Version3;
            o.Stores.MaxLengthForKeys = 450;
        });
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs)
            .UseApplicationServiceProvider(services.BuildServiceProvider()).Options;
        return new AppDbContext(options);
    }
}