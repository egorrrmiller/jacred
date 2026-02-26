using FluentMigrator;
using JacRed.Infrastructure.Migrations.Configurations;

namespace JacRed.Infrastructure.Migrations;

[Migration(3)]
public class AddLastRefreshTime : Migration
{
    public override void Up()
    {
        Alter.Table("search_queries")
            .InSchema(DbSchema.Name)
            .AddColumn("last_refresh_time").AsDateTimeOffset().Nullable();
    }

    public override void Down()
    {
        Delete.Column("last_refresh_time")
            .FromTable("search_queries")
            .InSchema(DbSchema.Name);
    }
}
