using FluentMigrator;
using JacRed.Infrastructure.Migrations.Configurations;

namespace JacRed.Infrastructure.Migrations;

[Migration(202602150500)]
public class DeleteSearchHistory : Migration
{
    public override void Up()
    {
        var schema = DbSchema.Name;
        Delete.Table("search_history").InSchema(schema);
    }

    public override void Down()
    {
        
    }
}