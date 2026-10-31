using FluentMigrator;
using Nop.Data.Migrations;

namespace Nop.Plugin.Payments.Lunixi.Data;

[NopMigration("2026/10/31 18:00:01", "Payments.Lunixi operation retention fence")]
public sealed class OperationRetentionMigration : Migration
{
    public override void Up()
    {
        if (!Schema.Table("LunixiOperation").Column("CreatedUtc").Exists())
            Alter.Table("LunixiOperation").AddColumn("CreatedUtc").AsString(40).Nullable();
        // Unknown historical timestamps are deliberately not replaced with the current time.
    }
    public override void Down() { }
}
