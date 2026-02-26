using FluentMigrator;
using JacRed.Infrastructure.Migrations.Configurations;

namespace JacRed.Infrastructure.Migrations;

[Migration(4)]
public class AddTorrentFfprobe : Migration
{
    public override void Up()
    {
        var schema = DbSchema.Name;

        Alter.Table("torrents").InSchema(schema)
            .AddColumn("ffprobe").AsCustom("jsonb").Nullable()
            .AddColumn("ffprobe_attempts").AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
        var schema = DbSchema.Name;

        Delete.Column("ffprobe_attempts").FromTable("torrents").InSchema(schema);
        Delete.Column("ffprobe").FromTable("torrents").InSchema(schema);
    }
}
