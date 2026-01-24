using FluentMigrator.Runner;
using FluentMigrator.Runner.VersionTableInfo;
using Microsoft.Extensions.DependencyInjection;

namespace JacRed.Infrastructure.Migrations.Configurations;

public static class MigrationExtensions
{
    public static IServiceCollection AddJacRedMigrations(this IServiceCollection services, string connectionString)
    {
        services
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(InitSchema).Assembly).For.Migrations())
            .AddLogging(lb => lb.AddFluentMigratorConsole())
            .AddScoped<IVersionTableMetaData, JacRedVersionTable>();

        return services;
    }

    public static void RunJacRedMigrations(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();
    }
}