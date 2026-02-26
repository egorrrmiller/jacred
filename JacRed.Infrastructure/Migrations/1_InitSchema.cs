using FluentMigrator;
using JacRed.Infrastructure.Migrations.Configurations;

namespace JacRed.Infrastructure.Migrations;

[Migration(1)]
public class InitSchema : Migration
{
    public override void Up()
    {
        var schema = DbSchema.Name;
        Execute.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");
        Execute.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        Execute.Sql("CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\";");
        Execute.Sql($"CREATE SCHEMA IF NOT EXISTS {schema};");

        Create.Table("torrents").InSchema(schema)
            .WithColumn("id").AsGuid().PrimaryKey().WithDefaultValue(SystemMethods.NewGuid)
            .WithColumn("tracker_name").AsString().NotNullable()
            .WithColumn("types").AsCustom("text[]").NotNullable()
            .WithColumn("url").AsString().NotNullable().Unique()
            .WithColumn("title").AsString().NotNullable()
            .WithColumn("sid").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("pir").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("size_name").AsString().Nullable()
            .WithColumn("create_time").AsDateTimeOffset().NotNullable()
            .WithColumn("update_time").AsDateTimeOffset().NotNullable()
            .WithColumn("check_time").AsDateTimeOffset().NotNullable()
            .WithColumn("magnet").AsString().Nullable()
            .WithColumn("name").AsString().Nullable()
            .WithColumn("original_name").AsString().Nullable()
            .WithColumn("relased").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("languages").AsCustom("text[]").Nullable()
            .WithColumn("source_season_number").AsString().Nullable()
            .WithColumn("source_season_order").AsString().Nullable()
            .WithColumn("size").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("quality").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("video_type").AsString().Nullable()
            .WithColumn("voices").AsCustom("text[]").Nullable()
            .WithColumn("seasons").AsCustom("integer[]").Nullable()
            .WithColumn("search_tsv").AsCustom("tsvector").Nullable()
            .WithColumn("search_name").AsString().Nullable()
            .WithColumn("original_search_name").AsString().Nullable();

        Create.Index("ix_torrents_sid").OnTable("torrents").InSchema(schema).OnColumn("sid").Descending();
        Create.Index("ix_torrents_tracker_sid").OnTable("torrents").InSchema(schema).OnColumn("tracker_name")
            .Ascending().OnColumn("sid").Descending();
        Create.Index("ix_torrents_update_time").OnTable("torrents").InSchema(schema).OnColumn("update_time")
            .Descending();
        Create.Index("ix_torrents_check_time").OnTable("torrents").InSchema(schema).OnColumn("check_time").Descending();
        Create.Index("ix_torrents_search_name_eq").OnTable("torrents").InSchema(schema).OnColumn("search_name");
        Create.Index("ix_torrents_original_search_name_eq").OnTable("torrents").InSchema(schema)
            .OnColumn("original_search_name");

        Execute.Sql($"""
                     DO $$
                     BEGIN
                         IF to_regclass('{schema}.torrents') IS NOT NULL THEN
                             CREATE INDEX IF NOT EXISTS ix_torrents_title_trgm ON {schema}.torrents USING gin (title gin_trgm_ops);
                             CREATE INDEX IF NOT EXISTS ix_torrents_name_trgm ON {schema}.torrents USING gin (name gin_trgm_ops);
                             CREATE INDEX IF NOT EXISTS ix_torrents_original_name_trgm ON {schema}.torrents USING gin (original_name gin_trgm_ops);
                             CREATE INDEX IF NOT EXISTS ix_torrents_search_name_trgm ON {schema}.torrents USING gin (search_name gin_trgm_ops);
                             CREATE INDEX IF NOT EXISTS ix_torrents_original_search_name_trgm ON {schema}.torrents USING gin (original_search_name gin_trgm_ops);
                             CREATE INDEX IF NOT EXISTS ix_torrents_search_tsv ON {schema}.torrents USING gin (search_tsv);
                         END IF;
                     END;
                     $$;
                     """);

        Execute.Sql($"""
                     DO $$
                     BEGIN
                         IF to_regclass('{schema}.torrents') IS NOT NULL THEN
                             CREATE OR REPLACE FUNCTION {schema}.torrents_update_search_tsv()
                             RETURNS trigger AS
                             $func$
                             BEGIN
                                 NEW.search_tsv =
                                     setweight(to_tsvector('russian', coalesce(NEW.title, '')), 'A') ||
                                     setweight(to_tsvector('russian', coalesce(NEW.name, '')), 'B') ||
                                     setweight(to_tsvector('simple', coalesce(NEW.original_name, '')), 'C');
                                 RETURN NEW;
                             END;
                             $func$
                             LANGUAGE plpgsql;

                             DROP TRIGGER IF EXISTS trg_torrents_search_tsv ON {schema}.torrents;
                             CREATE TRIGGER trg_torrents_search_tsv
                                 BEFORE INSERT OR UPDATE OF title, name, original_name
                                 ON {schema}.torrents
                                 FOR EACH ROW
                                 EXECUTE FUNCTION {schema}.torrents_update_search_tsv();
                         END IF;
                     END;
                     $$;
                     """);

        Create.Table("search_queries").InSchema(schema)
            .WithColumn("query").AsString().PrimaryKey()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithDefaultValue(SystemMethods.CurrentUTCDateTime)
            .WithColumn("last_seen").AsDateTimeOffset().NotNullable().WithDefaultValue(SystemMethods.CurrentUTCDateTime)
            .WithColumn("hits").AsInt32().NotNullable().WithDefaultValue(1);

        Create.Index("ix_search_queries_last_seen").OnTable("search_queries").InSchema(schema).OnColumn("last_seen")
            .Descending();
    }

    public override void Down()
    {
        var schema = DbSchema.Name;
        Delete.Table("search_queries").InSchema(schema);
        Delete.Table("torrents").InSchema(schema);
        Execute.Sql($"DROP SCHEMA IF EXISTS {schema} CASCADE;");
        Execute.Sql("DROP EXTENSION IF EXISTS pg_trgm;");
        Execute.Sql("DROP EXTENSION IF EXISTS pgcrypto;");
    }
}
