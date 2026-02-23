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

        // Удаляем старый индекс (uid, tmdb_id)
        Delete.Index("IX_subscriptions_uid_tmdb_id").OnTable("subscriptions").InSchema(schema);

        // Создаем новый уникальный индекс (uid, tmdb_id, media)
        Create.Index("IX_subscriptions_uid_tmdb_id_media").OnTable("subscriptions").InSchema(schema)
            .OnColumn("uid").Ascending()
            .OnColumn("tmdb_id").Ascending()
            .OnColumn("media").Ascending()
            .WithOptions().Unique();
    }

    public override void Down()
    {
        var schema = DbSchema.Name;

        Delete.Index("IX_subscriptions_uid_tmdb_id_media").OnTable("subscriptions").InSchema(schema);

        Create.Index("IX_subscriptions_uid_tmdb_id").OnTable("subscriptions").InSchema(schema)
            .OnColumn("uid").Ascending()
            .OnColumn("tmdb_id").Ascending()
            .WithOptions().Unique();

        Delete.Column("media").FromTable("subscriptions").InSchema(schema);
    }
}
