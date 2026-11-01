using System.Text.Json;
using Nop.Plugin.Payments.Lunixi.Domain;
namespace Lunixi.NopCommerce.Tests;
public class MoneyStringTests
{
    [Fact]
    public void DecimalTextUsesCurrencyAndRejectsNumericEvidence()
    {
        foreach (var input in new[] { ("9007199254740993", "JPY"), ("90071992547409.93", "TRY"), ("9007199254740.993", "KWD") })
            Assert.Equal(9007199254740993L, MinorMoney.FromDecimalText(input.Item1, input.Item2));
        Assert.Equal(9007199254740993L, MinorMoney.JsonInteger(JsonSerializer.SerializeToElement("9007199254740993")));
        foreach (var text in new[] { "01", "-0", "+1", " 1", "1.0", "1e3", "9223372036854775808" })
            Assert.Throws<PaymentReviewException>(() => MinorMoney.JsonInteger(JsonSerializer.SerializeToElement(text)));
        Assert.Throws<PaymentReviewException>(() => MinorMoney.JsonInteger(JsonSerializer.SerializeToElement(1)));
    }
}
