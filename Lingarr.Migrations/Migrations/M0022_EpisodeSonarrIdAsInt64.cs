using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(22)]
public class M0022_EpisodeSonarrIdAsInt64 : Migration
{
    public override void Up()
    {
        // nexcrate's episode ids (version id * 100000000 + episode id) do not fit in 32 bits.
        // SQLite's INTEGER already holds 64-bit values.
        IfDatabase("mysql").Alter.Table("episodes")
            .AlterColumn("sonarr_id").AsInt64().NotNullable();

        IfDatabase("postgresql").Execute.Sql(
            "ALTER TABLE episodes ALTER COLUMN sonarr_id TYPE bigint");
    }

    public override void Down()
    {
        IfDatabase("mysql").Alter.Table("episodes")
            .AlterColumn("sonarr_id").AsInt32().NotNullable();

        IfDatabase("postgresql").Execute.Sql(
            "ALTER TABLE episodes ALTER COLUMN sonarr_id TYPE integer");
    }
}
