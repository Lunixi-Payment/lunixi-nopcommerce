using System.Data;
using System.Reflection;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Configuration;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Shipping;
using Nop.Core.Events;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Data.Configuration;
using Nop.Data.DataProviders;
using Nop.Data.Mapping;
using Nop.Plugin.Payments.Lunixi.Data;
using Nop.Plugin.Payments.Lunixi.Domain;
using Nop.Plugin.Payments.Lunixi.Services;
using Nop.Services.Catalog;
using Nop.Services.Html;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Shipping;
using System.Text.Json;

namespace Lunixi.NopCommerce.Tests;

// These tests use the official 4.90.8 MySqlNopDataProvider, entity mappings, repositories,
// OrderService and OrderProcessingService against an isolated real MariaDB schema.
internal sealed class NativeDatabase
{
    public readonly INopDataProvider Provider;
    public readonly NativeConnectionFactory Connections;
    public readonly PaymentLedger Ledger;
    public readonly IOrderService Orders;
    public readonly IOrderProcessingService Processing;
    public readonly NativeOrders Native;
    public readonly Mock<IEventPublisher> Publisher = new() { DefaultValueProvider = EmptyDefaults.Instance };
    public int PaidEvents { get; private set; }
    public Mock<IPaymentService> Payments { get; } = new() { DefaultValueProvider = EmptyDefaults.Instance };
    private readonly DataConfig configuration;

    public NativeDatabase()
    {
        var file = Environment.GetEnvironmentVariable("LUNIXI_TEST_DB_CONFIG") ?? throw new InvalidOperationException("Set LUNIXI_TEST_DB_CONFIG to an isolated lunixi_nop TEST database config file.");
        using var json = JsonDocument.Parse(File.ReadAllText(file)); var config = json.RootElement;
        if (config.GetProperty("database").GetString() != "lunixi_nop") throw new InvalidOperationException("Native tests require the dedicated lunixi_nop schema.");
        var connection = new MySqlConnector.MySqlConnectionStringBuilder
        {
            Server = config.GetProperty("host").GetString(), Port = (uint)config.GetProperty("port").GetInt32(),
            Database = "lunixi_nop", UserID = config.GetProperty("username").GetString(), Password = config.GetProperty("password").GetString(),
            SslMode = MySqlConnector.MySqlSslMode.Disabled, ConnectionTimeout = 10, DefaultCommandTimeout = 30,
        };
        configuration = new() { ConnectionString = connection.ConnectionString, DataProvider = DataProviderType.MySql, SQLCommandTimeout = 30 };
        Singleton<DataConfig>.Instance = configuration;
        var finder = new NativeTypeFinder(); Singleton<ITypeFinder>.Instance = finder;
        var services = new ServiceCollection().AddSingleton<ITypeFinder>(finder).BuildServiceProvider();
        EngineContext.Replace(new TestEngine(services));
        Provider = new MySqlNopDataProvider(); Connections = new(configuration); Ledger = new(Connections);
        var orderRepo = Repository<Order>(); var noteRepo = Repository<OrderNote>();
        Orders = new OrderService(EmptyDefaults.Object<IHtmlFormatter>(), EmptyDefaults.Object<IProductService>(),
            EmptyDefaults.Object<IRepository<Address>>(), EmptyDefaults.Object<IRepository<Nop.Core.Domain.Customers.Customer>>(), orderRepo,
            Repository<OrderItem>(), noteRepo, Repository<Product>(),
            EmptyDefaults.Object<IRepository<ProductWarehouseInventory>>(), EmptyDefaults.Object<IRepository<RecurringPayment>>(),
            EmptyDefaults.Object<IRepository<RecurringPaymentHistory>>(), EmptyDefaults.Object<IShipmentService>());
        Publisher.Setup(p => p.PublishAsync(It.IsAny<OrderPaidEvent>())).Callback(() => PaidEvents++).Returns(Task.CompletedTask);
        var constructor = typeof(OrderProcessingService).GetConstructors().Single();
        var args = constructor.GetParameters().Select(parameter => parameter.ParameterType == typeof(IOrderService) ? Orders :
            parameter.ParameterType == typeof(IEventPublisher) ? Publisher.Object : parameter.ParameterType == typeof(IPaymentService) ? Payments.Object : EmptyDefaults.Value(parameter.ParameterType)).ToArray();
        Processing = (IOrderProcessingService)constructor.Invoke(args);
        Native = new(Provider, Orders, Processing, Publisher.Object);
    }
    private IRepository<T> Repository<T>() where T : BaseEntity
    {
        var settings = new AppSettings([new DistributedCacheConfig()]);
        return new EntityRepository<T>(Publisher.Object, Provider, EmptyDefaults.Object<IShortTermCacheManager>(), EmptyDefaults.Object<IStaticCacheManager>(), settings);
    }
    public async Task Initialize()
    {
        foreach (var type in new[] { typeof(Order), typeof(OrderNote), typeof(OrderItem), typeof(Product), typeof(QueuedEmail) })
        {
            var descriptor = NopMappingSchema.GetEntityDescriptor(type);
            var fields = descriptor.Fields.Select(field => field.IsPrimaryKey ? $"`{field.Name}` INT PRIMARY KEY AUTO_INCREMENT" : $"`{field.Name}` {SqlType(field.Type)} NULL");
            await Execute($"CREATE TABLE IF NOT EXISTS `{descriptor.EntityName}` ({string.Join(',', fields)}) ENGINE=InnoDB");
        }
        // Run the plugin's actual FluentMigrator expressions, using the same native driver and SQL generator.
        var services = new ServiceCollection();
        services.AddFluentMigratorCore().ConfigureRunner(runner => runner.AddMySql5().WithGlobalConnectionString(configuration.ConnectionString)
            .ScanIn(typeof(SchemaMigration).Assembly).For.Migrations());
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<FluentMigrator.Runner.IMigrationRunner>().MigrateUp();
    }
    public async Task<Order> Order(decimal amount = 100.50m)
    {
        var order = new Order
        {
            OrderGuid = Guid.NewGuid(), StoreId = 1, CustomerId = 7, BillingAddressId = 10, CustomOrderNumber = "TEST-" + Guid.NewGuid().ToString("N"),
            PaymentMethodSystemName = "Payments.Lunixi", OrderTotal = amount, CustomerCurrencyCode = "TRY", CurrencyRate = 1m,
            PaymentStatus = PaymentStatus.Pending, OrderStatus = OrderStatus.Pending, ShippingStatus = ShippingStatus.NotYetShipped,
            CreatedOnUtc = DateTime.UtcNow,
        };
        await Provider.InsertEntityAsync(order); return order;
    }
    public async Task<CheckoutAttempt> Attempt(Order order)
    {
        var identity = new OrderIdentity("merchant-1", "nop:1:" + order.OrderGuid.ToString("N"), MinorMoney.FromDecimal(order.OrderTotal, "TRY"), "TRY");
        var attempt = new CheckoutAttempt { NativeOrderId = order.Id, ProfileId = Guid.NewGuid().ToString("N"), OrderReference = identity.Reference, MerchantId = identity.MerchantId, Currency = "TRY", BaseMinor = identity.AmountMinor, Fingerprint = identity.Fingerprint, OperationKey = "test:checkout:" + Guid.NewGuid().ToString("N"), PayloadCipher = "synthetic-encrypted-payload", CreatedUtc = DateTime.UtcNow };
        await Ledger.WithOrderAsync(order.Id, async () => { attempt.Id = await Ledger.AddAttemptAsync(attempt); await Ledger.BindPaymentAsync(attempt, "pay-" + order.Id, "synthetic-token-cipher"); return true; });
        attempt.PaymentId = "pay-" + order.Id; return attempt;
    }
    public async Task<long> Count(string table) => Convert.ToInt64(await Scalar("SELECT COUNT(*) FROM `" + table + "`"));
    public async Task Execute(string sql)
    {
        await using var connection = Connections.Create(); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
    }
    private async Task<object?> Scalar(string sql)
    {
        await using var connection = Connections.Create(); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync();
    }
    private static string SqlType(DbType value) => value switch
    {
        DbType.Int32 => "INT", DbType.Int64 => "BIGINT", DbType.Boolean => "TINYINT(1)", DbType.Decimal => "DECIMAL(18,4)",
        DbType.DateTime or DbType.DateTime2 => "DATETIME(6)", DbType.Guid => "CHAR(36)", _ => "LONGTEXT",
    };
    private sealed class TestEngine(IServiceProvider services) : NopEngine
    { public override IServiceProvider ServiceProvider => services; }
    private sealed class NativeTypeFinder : ITypeFinder
    {
        public IList<Assembly> GetAssemblies() => [typeof(INopDataProvider).Assembly];
        public Assembly GetAssemblyByName(string name) => GetAssemblies().Single(a => a.FullName == name);
        public IEnumerable<Type> FindClassesOfType<T>(bool onlyConcreteClasses = true) => FindClassesOfType(typeof(T), onlyConcreteClasses);
        public IEnumerable<Type> FindClassesOfType(Type type, bool onlyConcreteClasses = true) => GetAssemblies().SelectMany(a => a.GetTypes()).Where(t => type.IsAssignableFrom(t) && (!onlyConcreteClasses || t.IsClass && !t.IsAbstract));
    }
}

internal sealed class EmptyDefaults : DefaultValueProvider
{
    public static readonly EmptyDefaults Instance = new();
    public static T Object<T>() where T : class => new Mock<T> { DefaultValueProvider = Instance }.Object;
    public static object? Value(Type type)
    {
        if (type == typeof(Task)) return Task.CompletedTask;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type.GenericTypeArguments[0]).Invoke(null, [Value(type.GenericTypeArguments[0])]);
        if (type == typeof(string)) return "";
        if (type.IsArray) return Array.CreateInstance(type.GetElementType()!, 0);
        if (type.IsGenericType && new[] { typeof(IEnumerable<>), typeof(IList<>), typeof(ICollection<>), typeof(IReadOnlyList<>), typeof(IReadOnlyCollection<>) }.Contains(type.GetGenericTypeDefinition()))
            return Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GenericTypeArguments));
        if (type.IsInterface)
        {
            var mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))!; mock.DefaultValueProvider = Instance; return mock.Object;
        }
        return type.IsValueType || type.GetConstructor(Type.EmptyTypes) is not null ? Activator.CreateInstance(type) : null;
    }
    protected override object? GetDefaultValue(Type type, Mock mock) => Value(type);
}
