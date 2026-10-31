using System.Data.Common;
using System.Globalization;
using System.Transactions;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Data.Mapping;
using Nop.Plugin.Payments.Lunixi.Domain;

namespace Nop.Plugin.Payments.Lunixi.Data;

public sealed class PaymentLedger(NativeConnectionFactory factory)
{
    private string Q(string name) => factory.Quote(name);
    private string Fields(params string[] names) => string.Join(",", names.Select(Q));
    private static string Now => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    public async Task<T> WithOrderAsync<T>(int orderId, Func<Task<T>> action)
    {
        await factory.VerifyTransactionalStorageAsync();
        using var transaction = new TransactionScope(TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted, Timeout = TimeSpan.FromMinutes(2) }, TransactionScopeAsyncFlowOption.Enabled);
        var table = Q(NameCompatibilityManager.GetTableName(typeof(Order)));
        var sql = factory.Provider == DataProviderType.SqlServer
            ? $"SELECT {Q("Id")} FROM {table} WITH (UPDLOCK,HOLDLOCK) WHERE {Q("Id")}=@id"
            : $"SELECT {Q("Id")} FROM {table} WHERE {Q("Id")}=@id FOR UPDATE";
        if (await Scalar(sql, ("id", orderId)) is null) throw new PaymentReviewException();
        // Every command closes its connection before the next/native API call. The ambient transaction
        // retains the row lock and reuses the same enlisted native connection without parallel opens.
        var result = await action(); transaction.Complete(); return result;
    }

    public async Task SaveProfileAsync(string profileId, int storeId, string cipher)
    {
        await factory.VerifyTransactionalStorageAsync();
        await Execute($"INSERT INTO {Q("LunixiProfile")} ({Fields("ProfileId", "StoreId", "Cipher")}) VALUES (@id,@store,@cipher)", ("id", profileId), ("store", storeId), ("cipher", cipher));
    }
    public async Task<(int StoreId, string Cipher)?> GetProfileAsync(string profileId)
    {
        var rows = await Query($"SELECT {Fields("StoreId", "Cipher")} FROM {Q("LunixiProfile")} WHERE {Q("ProfileId")}=@id", reader => (reader.GetInt32(0), reader.GetString(1)), ("id", profileId));
        return rows.Count == 0 ? null : rows[0];
    }
    public async Task<CheckoutAttempt?> GetActiveAsync(int orderId)
    {
        var rows = await Query($"SELECT a.* FROM {Q("LunixiAttempt")} a JOIN {Q("LunixiOrderLedger")} l ON a.{Q("Id")}=l.{Q("ActiveAttemptId")} WHERE l.{Q("NativeOrderId")}=@id", ReadAttempt, ("id", orderId));
        return rows.FirstOrDefault();
    }
    public async Task<CheckoutAttempt?> FindPaymentAsync(string merchantId, string paymentId)
    {
        var rows = await Query($"SELECT * FROM {Q("LunixiAttempt")} WHERE {Q("MerchantId")}=@merchant AND {Q("PaymentId")}=@payment", ReadAttempt, ("merchant", merchantId), ("payment", paymentId));
        if (rows.Count > 1) throw new PaymentReviewException(); return rows.FirstOrDefault();
    }
    public async Task<int> AddAttemptAsync(CheckoutAttempt value)
    {
        var columns = new[] { "NativeOrderId", "ProfileId", "OrderReference", "MerchantId", "Currency", "BaseMinor", "Fingerprint", "OperationKey", "PayloadCipher", "State", "GrossMinor", "RefundedMinor", "CreatedUtc" };
        var id = await InsertId("LunixiAttempt", columns, [value.NativeOrderId, value.ProfileId, value.OrderReference, value.MerchantId, value.Currency, value.BaseMinor, value.Fingerprint, value.OperationKey, value.PayloadCipher, value.State, 0L, 0L, value.CreatedUtc.ToString("O", CultureInfo.InvariantCulture)]);
        if (await Scalar($"SELECT {Q("NativeOrderId")} FROM {Q("LunixiOrderLedger")} WHERE {Q("NativeOrderId")}=@id", ("id", value.NativeOrderId)) is null)
            await Execute($"INSERT INTO {Q("LunixiOrderLedger")} ({Fields("NativeOrderId", "ActiveAttemptId")}) VALUES (@order,@attempt)", ("order", value.NativeOrderId), ("attempt", id));
        else await Execute($"UPDATE {Q("LunixiOrderLedger")} SET {Q("ActiveAttemptId")}=@attempt WHERE {Q("NativeOrderId")}=@order", ("order", value.NativeOrderId), ("attempt", id));
        return id;
    }
    public async Task BindPaymentAsync(CheckoutAttempt value, string paymentId, string tokenCipher)
    {
        if (await Execute($"UPDATE {Q("LunixiAttempt")} SET {Q("PaymentId")}=@payment,{Q("TokenCipher")}=@token,{Q("State")}='pending' WHERE {Q("Id")}=@id AND ({Q("PaymentId")} IS NULL OR {Q("PaymentId")}=@payment)", ("payment", paymentId), ("token", tokenCipher), ("id", value.Id)) != 1)
            throw new PaymentReviewException();
    }
    public async Task UpdateSettlementAsync(int attemptId, Settlement value)
        => _ = await Execute($"UPDATE {Q("LunixiAttempt")} SET {Q("State")}=@state,{Q("GrossMinor")}=@gross,{Q("RefundedMinor")}=@refund,{Q("LastCheckedUtc")}=@now WHERE {Q("Id")}=@id", ("state", value.State), ("gross", value.GrossMinor), ("refund", value.RefundedMinor), ("now", Now), ("id", attemptId));
    public async Task TouchAsync(int orderId)
        => _ = await Execute($"UPDATE {Q("LunixiAttempt")} SET {Q("LastCheckedUtc")}=@now WHERE {Q("NativeOrderId")}=@order", ("now", Now), ("order", orderId));

    public async Task<PendingOperation?> GetPendingOperationAsync(int attemptId)
    {
        var rows = await Query($"SELECT * FROM {Q("LunixiOperation")} WHERE {Q("AttemptId")}=@id AND {Q("State")}<>'applied' ORDER BY {Q("Id")}", r => new PendingOperation(Int(r, "Id"), Int(r, "AttemptId"), Text(r, "Kind"), Long(r, "AmountMinor"), Long(r, "BaselineRefunded"), Text(r, "OperationKey"), Text(r, "State"), r["CreatedUtc"] is DBNull ? DateTime.MinValue : DateTime.Parse(Text(r, "CreatedUtc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)), ("id", attemptId));
        if (rows.Count > 1) throw new PaymentReviewException(); return rows.FirstOrDefault();
    }
    public async Task<PendingOperation> AddOperationAsync(int attemptId, string kind, long amount, long baseline)
    {
        var key = "nop:" + kind + ":" + Guid.NewGuid().ToString("N");
        var created = DateTime.UtcNow;
        var id = await InsertId("LunixiOperation", ["AttemptId", "Kind", "AmountMinor", "BaselineRefunded", "OperationKey", "State", "CreatedUtc"], [attemptId, kind, amount, baseline, key, "reserved", created.ToString("O", CultureInfo.InvariantCulture)]);
        return new(id, attemptId, kind, amount, baseline, key, "reserved", created);
    }
    public async Task SetOperationStateAsync(int id, string state)
        => _ = await Execute($"UPDATE {Q("LunixiOperation")} SET {Q("State")}=@state WHERE {Q("Id")}=@id", ("id", id), ("state", state));
    public async Task<bool> InboxCompletedAsync(string profileId, string eventId, string hash)
    {
        var stored = await Scalar($"SELECT {Q("BodyHash")} FROM {Q("LunixiInbox")} WHERE {Q("ProfileId")}=@profile AND {Q("EventId")}=@event", ("profile", profileId), ("event", eventId));
        if (stored is null) return false;
        if (Convert.ToString(stored, CultureInfo.InvariantCulture) != hash) throw new GatewayFault("webhook_conflict", statusCode: 400);
        return true;
    }
    public async Task CompleteInboxAsync(string profileId, string eventId, string hash)
        => _ = await Execute($"INSERT INTO {Q("LunixiInbox")} ({Fields("ProfileId", "EventId", "BodyHash", "CompletedUtc")}) VALUES (@profile,@event,@hash,@now)", ("profile", profileId), ("event", eventId), ("hash", hash), ("now", Now));
    public async Task<IReadOnlyList<int>> PendingOrderIdsAsync()
    {
        // Separate rotating budgets keep a large settled history from delaying new orders,
        // while continuing to discover gateway-side refunds on already paid orders.
        async Task<List<int>> Batch(bool unresolved, int count)
        {
            var top = factory.Provider == DataProviderType.SqlServer ? $"TOP {count} " : "";
            var limit = factory.Provider == DataProviderType.SqlServer ? "" : $" LIMIT {count}";
            var operation = $"EXISTS (SELECT 1 FROM {Q("LunixiOperation")} o WHERE o.{Q("AttemptId")}=a.{Q("Id")} AND o.{Q("State")}<>'applied')";
            var condition = unresolved
                ? $"(a.{Q("State")} IN ('pending','authorized','review') OR {operation})"
                : $"(a.{Q("State")} IN ('paid','partially_refunded') AND NOT {operation})";
            return await Query($"SELECT {top}a.{Q("NativeOrderId")} FROM {Q("LunixiAttempt")} a JOIN {Q("LunixiOrderLedger")} l ON a.{Q("Id")}=l.{Q("ActiveAttemptId")} WHERE a.{Q("PaymentId")} IS NOT NULL AND {condition} ORDER BY COALESCE(a.{Q("LastCheckedUtc")},a.{Q("CreatedUtc")}),a.{Q("Id")}{limit}", r => r.GetInt32(0));
        }
        var active = await Batch(true, 80);
        active.AddRange(await Batch(false, 20));
        return active;
    }
    private async Task<int> InsertId(string table, string[] fields, object?[] values)
    {
        var parameters = fields.Select((_, i) => "@v" + i).ToArray();
        var sql = $"INSERT INTO {Q(table)} ({Fields(fields)}) VALUES ({string.Join(',', parameters)})";
        if (factory.Provider == DataProviderType.PostgreSQL) sql += $" RETURNING {Q("Id")}";
        if (factory.Provider == DataProviderType.SqlServer) sql += ";SELECT CAST(SCOPE_IDENTITY() AS int)";
        if (factory.Provider == DataProviderType.MySql) sql += ";SELECT LAST_INSERT_ID()";
        return Convert.ToInt32(await Scalar(sql, values.Select((value, i) => ("v" + i, value)).ToArray()), CultureInfo.InvariantCulture);
    }
    private async Task<object?> Scalar(string sql, params (string Name, object? Value)[] values)
    {
        await using var connection = factory.Create(); await connection.OpenAsync(); await using var command = Command(connection, sql, values);
        var value = await command.ExecuteScalarAsync(); return value is DBNull ? null : value;
    }
    private async Task<int> Execute(string sql, params (string Name, object? Value)[] values)
    {
        await using var connection = factory.Create(); await connection.OpenAsync(); await using var command = Command(connection, sql, values); return await command.ExecuteNonQueryAsync();
    }
    private async Task<List<T>> Query<T>(string sql, Func<DbDataReader, T> map, params (string Name, object? Value)[] values)
    {
        await using var connection = factory.Create(); await connection.OpenAsync(); await using var command = Command(connection, sql, values); await using var reader = await command.ExecuteReaderAsync();
        var result = new List<T>(); while (await reader.ReadAsync()) result.Add(map(reader)); return result;
    }
    private static DbCommand Command(DbConnection connection, string sql, (string Name, object? Value)[] values)
    {
        var command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = 30;
        foreach (var item in values) { var parameter = command.CreateParameter(); parameter.ParameterName = item.Name; parameter.Value = item.Value ?? DBNull.Value; command.Parameters.Add(parameter); }
        return command;
    }
    private static int Int(DbDataReader r, string name) => Convert.ToInt32(r[name], CultureInfo.InvariantCulture);
    private static long Long(DbDataReader r, string name) => Convert.ToInt64(r[name], CultureInfo.InvariantCulture);
    private static string Text(DbDataReader r, string name) => Convert.ToString(r[name], CultureInfo.InvariantCulture)!;
    private static string? Optional(DbDataReader r, string name) => r[name] is DBNull ? null : Text(r, name);
    private static CheckoutAttempt ReadAttempt(DbDataReader r) => new()
    {
        Id = Int(r, "Id"),
        NativeOrderId = Int(r, "NativeOrderId"),
        ProfileId = Text(r, "ProfileId"),
        OrderReference = Text(r, "OrderReference"),
        MerchantId = Text(r, "MerchantId"),
        Currency = Text(r, "Currency"),
        BaseMinor = Long(r, "BaseMinor"),
        Fingerprint = Text(r, "Fingerprint"),
        OperationKey = Text(r, "OperationKey"),
        PayloadCipher = Text(r, "PayloadCipher"),
        PaymentId = Optional(r, "PaymentId"),
        TokenCipher = Optional(r, "TokenCipher"),
        State = Text(r, "State"),
        GrossMinor = Long(r, "GrossMinor"),
        RefundedMinor = Long(r, "RefundedMinor"),
        CreatedUtc = DateTime.Parse(Text(r, "CreatedUtc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
    };
}
