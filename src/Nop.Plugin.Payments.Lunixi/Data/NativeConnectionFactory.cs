using System.Data.Common;
using Nop.Data;
using Nop.Data.Configuration;
using Nop.Data.Mapping;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Messages;

namespace Nop.Plugin.Payments.Lunixi.Data;

public sealed class NativeConnectionFactory
{
    private readonly DataConfig configuration;
    private bool transactionalStorageVerified;
    public NativeConnectionFactory() : this(DataSettingsManager.LoadSettings()) { }
    public NativeConnectionFactory(DataConfig configuration)
    {
        this.configuration = configuration;
        var enlisted = configuration.DataProvider switch
        {
            DataProviderType.MySql => new MySqlConnector.MySqlConnectionStringBuilder(configuration.ConnectionString).AutoEnlist,
            DataProviderType.PostgreSQL => new Npgsql.NpgsqlConnectionStringBuilder(configuration.ConnectionString).Enlist,
            DataProviderType.SqlServer => new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(configuration.ConnectionString).Enlist,
            _ => false,
        };
        if (!enlisted) throw new InvalidOperationException("Lunixi requires native database connections to enlist in ambient transactions.");
    }
    public DataProviderType Provider => configuration.DataProvider;
    public DbConnection Create() => Provider switch
    {
        DataProviderType.PostgreSQL => new Npgsql.NpgsqlConnection(configuration.ConnectionString),
        DataProviderType.SqlServer => new Microsoft.Data.SqlClient.SqlConnection(configuration.ConnectionString),
        DataProviderType.MySql => new MySqlConnector.MySqlConnection(configuration.ConnectionString),
        _ => throw new InvalidOperationException("Lunixi requires a supported transactional nopCommerce database."),
    };
    public string Quote(string name) => Provider == DataProviderType.MySql ? "`" + name.Replace("`", "``", StringComparison.Ordinal) + "`" : "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    public async Task VerifyTransactionalStorageAsync()
    {
        if (transactionalStorageVerified || Provider != DataProviderType.MySql) return;
        var required = new[] { NameCompatibilityManager.GetTableName(typeof(Order)), NameCompatibilityManager.GetTableName(typeof(OrderNote)), NameCompatibilityManager.GetTableName(typeof(QueuedEmail)), "LunixiProfile", "LunixiOrderLedger", "LunixiAttempt", "LunixiOperation", "LunixiInbox" };
        await using var connection = Create(); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandTimeout = 30;
        command.CommandText = "SELECT TABLE_NAME,ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN (" + string.Join(',', required.Select((_, i) => "@t" + i)) + ")";
        for (var i = 0; i < required.Length; i++) { var parameter = command.CreateParameter(); parameter.ParameterName = "t" + i; parameter.Value = required[i]; command.Parameters.Add(parameter); }
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase); await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!string.Equals(reader.GetString(1), "InnoDB", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Lunixi financial, order, note and queued-email tables must use InnoDB.");
            found.Add(reader.GetString(0));
        }
        if (required.Any(table => !found.Contains(table))) throw new InvalidOperationException("Required native transactional payment tables are missing.");
        transactionalStorageVerified = true;
    }
}
