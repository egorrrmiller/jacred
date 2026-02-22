using FluentMigrator;
using JacRed.Infrastructure.Migrations.Configurations;

namespace JacRed.Infrastructure.Migrations;

[Migration(6)]
public class RecreateQueriesAndSubscriptions : Migration
{
    public override void Up()
    {
        var schema = DbSchema.Name;

        // 1. Удаляем старую таблицу (если есть)
        if (Schema.Schema(schema).Table("search_queries").Exists())
        {
            Delete.Table("search_queries").InSchema(schema);
        }
        
        // 2. Создаем таблицу queries
        Create.Table("queries").InSchema(schema)
            .WithColumn("tmdb_id").AsInt64().PrimaryKey() // PK
            .WithColumn("query").AsString().NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("last_seen").AsDateTimeOffset().NotNullable()
            .WithColumn("hits").AsInt64().NotNullable()
            .WithColumn("last_refresh_time").AsDateTimeOffset().Nullable();

        // 3. Создаем таблицу subscriptions
        Create.Table("subscriptions").InSchema(schema)
            .WithColumn("id").AsGuid().PrimaryKey() // PK
            .WithColumn("tmdb_id").AsInt64().NotNullable() // FK
            .WithColumn("uid").AsString().NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable();

        // 4. FK subscriptions -> queries
        Create.ForeignKey("FK_subscriptions_queries")
            .FromTable("subscriptions").InSchema(schema).ForeignColumn("tmdb_id")
            .ToTable("queries").InSchema(schema).PrimaryColumn("tmdb_id")
            .OnDelete(System.Data.Rule.Cascade);
            
        // Уникальный индекс для подписок (uid + tmdb_id)
        Create.Index("IX_subscriptions_uid_tmdb_id").OnTable("subscriptions").InSchema(schema)
            .OnColumn("uid").Ascending()
            .OnColumn("tmdb_id").Ascending()
            .WithOptions().Unique();

        // Индекс на tmdb_id (для поиска подписчиков по фильму)
        Create.Index("IX_subscriptions_tmdb_id").OnTable("subscriptions").InSchema(schema)
            .OnColumn("tmdb_id");

        // Индекс на uid (для поиска подписок пользователя)
        Create.Index("IX_subscriptions_uid").OnTable("subscriptions").InSchema(schema)
            .OnColumn("uid");
    }

    public override void Down()
    {
        var schema = DbSchema.Name;
        Delete.Table("subscriptions").InSchema(schema);
        Delete.Table("queries").InSchema(schema);
        
        // Восстанавливаем старую таблицу
        Create.Table("search_queries").InSchema(schema)
            .WithColumn("query").AsString().PrimaryKey()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("last_seen").AsDateTimeOffset().NotNullable()
            .WithColumn("hits").AsInt64().NotNullable()
            .WithColumn("last_refresh_time").AsDateTimeOffset().Nullable();
    }
}
