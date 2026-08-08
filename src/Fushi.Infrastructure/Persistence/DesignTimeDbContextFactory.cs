using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fushi.Infrastructure.Persistence;

/// <summary>
/// Builds a <see cref="FushiDbContext"/> for the <c>dotnet ef</c> tooling.
/// </summary>
/// <remarks>
/// Migrations are generated against the model, not against a server, so nothing
/// here needs to reach a live database — but Npgsql still insists on a
/// syntactically valid connection string before it will build a provider. Hence
/// the placeholder below.
/// <br/>
/// The alternative is letting the tooling start <c>Fushi.Host</c> and borrow its
/// service provider. That works, and it is what the EF documentation suggests,
/// but it means generating a migration boots the Discord gateway, reads the bot
/// token, and fails on a developer machine that has neither. Keeping design-time
/// construction here costs one small class and makes <c>dotnet ef</c> work in a
/// fresh clone with no configuration at all.
/// <br/>
/// Set <c>FUSHI_DESIGN_TIME_CONNECTION</c> to point the tooling at a real server
/// when scaffolding from an existing database or running
/// <c>dotnet ef database update</c> by hand.
/// </remarks>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FushiDbContext>
{
    /// <summary>
    /// Used when no override is supplied. Never connected to during
    /// <c>migrations add</c>, which reads the model rather than the server.
    /// </summary>
    private const string PLACEHOLDER_CONNECTION =
        "Host=localhost;Port=5432;Database=fushi;Username=fushi;Password=fushi";

    /// <summary>
    /// Creates the context the tooling will inspect.
    /// </summary>
    /// <param name="args">
    /// Arguments passed after <c>--</c> on the <c>dotnet ef</c> command line.
    /// Unused; the connection comes from the environment instead, so that it does
    /// not have to be repeated on every invocation.
    /// </param>
    /// <returns>A context configured against PostgreSQL.</returns>
    public FushiDbContext CreateDbContext(string[] args)
    {
        string connection =
            Environment.GetEnvironmentVariable("FUSHI_DESIGN_TIME_CONNECTION")
            ?? PLACEHOLDER_CONNECTION;

        DbContextOptions<FushiDbContext> options =
            new DbContextOptionsBuilder<FushiDbContext>()
                .UseNpgsql(
                    connection,
                    npgsql => npgsql.MigrationsHistoryTable("__migrations_history"))
                .Options;

        return new FushiDbContext(options);
    }
}
