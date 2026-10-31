using FluentMigrator;
using Nop.Data.Migrations;

namespace Nop.Plugin.Payments.Lunixi.Data;

[NopMigration("2026/10/31 18:00:00", "Payments.Lunixi durable payment history", MigrationProcessType.Installation)]
public sealed class SchemaMigration : Migration
{
    public override void Up()
    {
        if (!Schema.Table("LunixiProfile").Exists())
            Create.Table("LunixiProfile").WithColumn("ProfileId").AsString(32).PrimaryKey()
                .WithColumn("StoreId").AsInt32().NotNullable().WithColumn("Cipher").AsString(int.MaxValue).NotNullable();
        if (!Schema.Table("LunixiOrderLedger").Exists())
            Create.Table("LunixiOrderLedger").WithColumn("NativeOrderId").AsInt32().PrimaryKey()
                .WithColumn("ActiveAttemptId").AsInt32().Nullable();
        if (!Schema.Table("LunixiAttempt").Exists())
        {
            Create.Table("LunixiAttempt").WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("NativeOrderId").AsInt32().NotNullable().WithColumn("ProfileId").AsString(32).NotNullable()
                .WithColumn("OrderReference").AsString(128).NotNullable().WithColumn("MerchantId").AsString(256).NotNullable()
                .WithColumn("Currency").AsString(3).NotNullable().WithColumn("BaseMinor").AsInt64().NotNullable()
                .WithColumn("Fingerprint").AsString(64).NotNullable().WithColumn("OperationKey").AsString(128).NotNullable().Unique()
                .WithColumn("PayloadCipher").AsString(int.MaxValue).NotNullable().WithColumn("PaymentId").AsString(256).Nullable()
                .WithColumn("TokenCipher").AsString(int.MaxValue).Nullable().WithColumn("State").AsString(32).NotNullable()
                .WithColumn("GrossMinor").AsInt64().NotNullable().WithColumn("RefundedMinor").AsInt64().NotNullable()
                .WithColumn("CreatedUtc").AsString(40).NotNullable().WithColumn("LastCheckedUtc").AsString(40).Nullable();
            Create.Index("IX_LunixiAttempt_Order").OnTable("LunixiAttempt").OnColumn("NativeOrderId");
            Create.Index("IX_LunixiAttempt_Payment").OnTable("LunixiAttempt").OnColumn("PaymentId");
        }
        if (!Schema.Table("LunixiOperation").Exists())
        {
            Create.Table("LunixiOperation").WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("AttemptId").AsInt32().NotNullable().WithColumn("Kind").AsString(16).NotNullable()
                .WithColumn("AmountMinor").AsInt64().NotNullable().WithColumn("BaselineRefunded").AsInt64().NotNullable()
                .WithColumn("OperationKey").AsString(128).NotNullable().Unique().WithColumn("State").AsString(32).NotNullable();
            Create.Index("IX_LunixiOperation_Attempt").OnTable("LunixiOperation").OnColumn("AttemptId");
        }
        if (!Schema.Table("LunixiInbox").Exists())
            Create.Table("LunixiInbox").WithColumn("ProfileId").AsString(32).NotNullable()
                .WithColumn("EventId").AsString(256).NotNullable().WithColumn("BodyHash").AsString(64).NotNullable()
                .WithColumn("CompletedUtc").AsString(40).NotNullable();
        if (!Schema.Table("LunixiInbox").Index("IX_LunixiInbox_Unique").Exists())
            Create.Index("IX_LunixiInbox_Unique").OnTable("LunixiInbox").OnColumn("ProfileId").Ascending().OnColumn("EventId").Ascending().WithOptions().Unique();
    }
    // Financial history and encrypted credentials are deliberately retained on uninstall/rollback.
    public override void Down() { }
}
