using System.Linq.Expressions;
using System.Reflection;
using System.Text;

using Fushi.Core.Abstractions;
using Fushi.Core.Entities.Audits;
using Fushi.Core.Entities.Cycles;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Entities.Submissions;
using Fushi.Infrastructure.Persistence.Converters;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Fushi.Infrastructure.Persistence;

/// <summary>
/// The database session, and the single description of how the domain maps onto
/// PostgreSQL.
/// </summary>
/// <remarks>
/// Entity configuration lives in one class per entity under
/// <c>Persistence/Configurations</c> and is discovered by assembly scan, not
/// written here. A five-hundred-line <c>OnModelCreating</c> is the usual outcome
/// of doing otherwise, and it becomes the file nobody wants to touch.
/// <br/>
/// Several conventions are applied model-wide because applying them per entity or
/// per property is a standing invitation to miss one:
/// <br/>
/// Every <see cref="ulong"/> is stored through <see cref="SnowflakeConverter"/>,
/// since PostgreSQL has no unsigned 64-bit type. Every table and column is named
/// in <c>snake_case</c>, which is what PostgreSQL folds unquoted identifiers to
/// anyway — leaving them in PascalCase means every hand-written query needs
/// quotes around every name. Every <see cref="IDeletable"/> is filtered to its
/// live rows, and every <see cref="IMentionable"/> keeps its mention out of the
/// schema.
/// </remarks>
/// <param name="options">The configured options, supplied by the host.</param>
public sealed class FushiDbContext(DbContextOptions<FushiDbContext> options)
    : DbContext(options)
{
    /// <summary>
    /// The table Entity Framework records applied migrations in.
    /// </summary>
    /// <remarks>
    /// Named in one place because the runtime and the <c>dotnet ef</c> tooling
    /// configure the provider separately. Two spellings would leave each of them
    /// reading a different history and re-applying migrations the other had
    /// already run.
    /// </remarks>
    public const string MIGRATIONS_HISTORY_TABLE = "__migrations";

    /// <summary>
    /// Gets the per-guild configuration.
    /// </summary>
    public DbSet<Guild> Guilds => Set<Guild>();

    /// <summary>
    /// Gets the voting grants.
    /// </summary>
    public DbSet<VotingPermission> VotingPermissions => Set<VotingPermission>();

    /// <summary>
    /// Gets the voting cycles.
    /// </summary>
    public DbSet<Cycle> Cycles => Set<Cycle>();

    /// <summary>
    /// Gets the submissions.
    /// </summary>
    public DbSet<Submission> Submissions => Set<Submission>();

    /// <summary>
    /// Gets the individual votes.
    /// </summary>
    public DbSet<Vote> Votes => Set<Vote>();

    /// <summary>
    /// Gets the audit trail.
    /// </summary>
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <inheritdoc/>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<ulong>().HaveConversion<SnowflakeConverter>();

        // Every timestamp in the domain is UTC by construction, because handlers
        // read the clock through TimeProvider.GetUtcNow. Declaring the store type
        // makes that explicit in the schema and lets Npgsql reject a non-UTC value
        // outright instead of silently shifting it.
        configurationBuilder.Properties<DateTimeOffset>().HaveColumnType("timestamptz");
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        ApplyMarkerInterfaceConventions(modelBuilder);
        ApplySnakeCaseNames(modelBuilder);
    }

    /// <summary>
    /// Applies the mapping each of the audit marker interfaces implies.
    /// </summary>
    /// <remarks>
    /// An <see cref="IMentionable"/> builds its mention from a snowflake it
    /// already stores, so the mention is presentation rather than state. Mapping it
    /// would create a column that could disagree with what it was derived from.
    /// <br/>
    /// An <see cref="IDeletable"/> is never physically removed, so ordinary reads
    /// have to exclude the rows that are gone. Doing that here rather than in each
    /// entity's configuration means a new soft-deletable entity is filtered the day
    /// it is added, instead of leaking deleted rows until somebody notices the
    /// missing line. The filter tests the timestamp because
    /// <see cref="IDeletable.IsDeleted"/> is computed from it and is not stored.
    /// </remarks>
    /// <param name="modelBuilder">The model under construction.</param>
    private static void ApplyMarkerInterfaceConventions(ModelBuilder modelBuilder)
    {
        // Configuring an entity can add types to the model, so the set is taken
        // before the loop rather than iterated as it changes.
        var clrTypes = modelBuilder.Model
            .GetEntityTypes()
            .Select(entity => entity.ClrType)
            .ToList();

        foreach (Type clrType in clrTypes)
        {
            if (typeof(IMentionable).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType).Ignore(nameof(IMentionable.Mention));
            }

            if (typeof(IDeletable).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType).HasQueryFilter(LiveRowsOnly(clrType));
            }
        }
    }

    /// <summary>
    /// Builds <c>entity =&gt; entity.DeletedAt == null</c> for a type known only at
    /// run time.
    /// </summary>
    /// <param name="clrType">The entity type the filter applies to.</param>
    /// <returns>The filter expression.</returns>
    private static LambdaExpression LiveRowsOnly(Type clrType)
    {
        ParameterExpression entity = Expression.Parameter(clrType, "entity");

        return Expression.Lambda(
            Expression.Equal(
                Expression.Property(entity, nameof(IDeletable.DeletedAt)),
                Expression.Constant(null, typeof(DateTimeOffset?))),
            entity);
    }

    private static void ApplySnakeCaseNames(ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is { } table)
            {
                entity.SetTableName(ToSnakeCase(table));
            }

            foreach (IMutableProperty property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }

            foreach (IMutableKey key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName() ?? string.Empty));
            }

            foreach (IMutableForeignKey foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(
                    ToSnakeCase(foreignKey.GetConstraintName() ?? string.Empty));
            }

            foreach (IMutableIndex index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName() ?? string.Empty));
            }
        }
    }

    /// <summary>
    /// Rewrites a PascalCase identifier in <c>snake_case</c>.
    /// </summary>
    /// <remarks>
    /// Inserts an underscore before each capital that follows a lower-case letter
    /// or digit, and before the last capital of a run that is followed by a
    /// lower-case letter. That second rule is what turns <c>GuildID</c> into
    /// <c>guild_id</c> rather than <c>guild_i_d</c>, and <c>IANAName</c> into
    /// <c>iana_name</c> rather than <c>i_a_n_a_name</c>.
    /// </remarks>
    /// <param name="name">The identifier to rewrite.</param>
    /// <returns>The <c>snake_case</c> form.</returns>
    internal static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        StringBuilder builder = new(name.Length + 8);

        for (int index = 0; index < name.Length; index++)
        {
            char current = name[index];

            if (char.IsUpper(current) && index > 0)
            {
                char previous = name[index - 1];
                bool followsWord = char.IsLower(previous) || char.IsDigit(previous);
                bool endsAcronym = char.IsUpper(previous)
                    && index + 1 < name.Length
                    && char.IsLower(name[index + 1]);

                if ((followsWord || endsAcronym) && builder[^1] != '_')
                {
                    builder.Append('_');
                }
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}
