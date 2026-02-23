using FluentMigrator;
using JacRed.Infrastructure.Migrations.Configurations;

namespace JacRed.Infrastructure.Migrations;

[Migration(7)]
public class AddMediaToSubscriptions : Migration
{
    public override void Up()
    {
        var schema = DbSchema.Name;

        Alter.Table("subscriptions").InSchema(schema)
            .AddColumn("media").AsString().Nullable();

        Execute.Sql($"UPDATE {schema}.subscriptions SET media = '' WHERE media IS NULL;");

        Alter.Column("media").OnTable("subscriptions").InSchema(schema).AsString().NotNullable();
    }

    public override void Down()
    {
        var schema = DbSchema.Name;
        Delete.Column("media").FromTable("subscriptions").InSchema(schema);
    }
}
