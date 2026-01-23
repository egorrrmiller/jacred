using FluentMigrator.Runner.VersionTableInfo;
using JacRed.Infrastructure.Migrations.Configurations;

namespace JacRed.Infrastructure.Migrations;

public class JacRedVersionTable : IVersionTableMetaData
{
    public string SchemaName => DbSchema.Name;
    public string TableName => "VersionInfo";
    public string ColumnName => "Version";
    public string UniqueIndexName => "UC_VersionInfo";
    public string AppliedOnColumnName => "AppliedOn";
    public string DescriptionColumnName => "FluentMigrator version table";
    public bool OwnsSchema => true;
    public object? ApplicationContext { get; set; }
}
