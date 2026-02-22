using FluentMigrator;
using JacRed.Infrastructure.Migrations.Configurations;

namespace JacRed.Infrastructure.Migrations;

[Migration(2)]
public class AddSearchHistory : Migration
{
    public override void Up()
    {
        var schema = DbSchema.Name;

        Create.Table("search_history").InSchema(schema)
            .WithColumn("query").AsString().PrimaryKey()
            .WithColumn("last_search_time").AsDateTimeOffset().NotNullable()
            .WithColumn("trackers_hash").AsString().NotNullable();
    }

    public override void Down()
    {
        var schema = DbSchema.Name;
        Delete.Table("search_history").InSchema(schema);
    }
}
