namespace Nop.Plugin.Payments.Lunixi.Infrastructure;

public static class LocaleResources
{
    public const string Prefix = "Plugins.Payments.Lunixi.";
    public static readonly IReadOnlyDictionary<string, string> English = Values(
        "Enabled", "Enable Lunixi payments", "MerchantId", "Merchant ID", "KeyId", "Merchant SECRET key identifier",
        "PrivateKey", "Ed25519 private key (PKCS#8 PEM)", "WebhookSecret", "Webhook signing secret",
        "Hosted", "Use optional hosted checkout (backend paymentPageUrl)", "Preauthorize", "Authorize now, capture later",
        "Methods", "Available methods, installments, insurance and split payments are managed in your Lunixi panel.",
        "SecretHint", "Leave secret fields blank to keep the saved values. Secrets are encrypted; previous credential profiles remain available for reconciliation and refunds.",
        "ServerKey", "Configure LUNIXI_NOP_ENCRYPTION_KEY on every application server: a backed-up base64 key containing 32 random bytes. Keep it outside the database and source repository.",
        "ServerKeyMissing", "The server encryption key is missing. Payments remain disabled until it is configured.",
        "WebhookUrl", "Webhook URL", "Description", "Pay securely with Lunixi. Settlement uses the store's primary currency.",
        "Pay", "Continue to secure payment", "Pending", "Your payment is being verified. This page cannot confirm payment by itself.",
        "Order", "View order", "Unavailable", "Payment is unavailable or requires reconciliation. Please check your order status before trying again.",
        "OperationPending", "The operation is pending or already processed. Refresh the order and reconcile before retrying; do not create a new payment.",
        "InvalidSettings", "The configuration could not be validated. Check the server encryption key and Lunixi credentials.",
        "Saved", "Lunixi settings were saved.", "UnsupportedRecurring", "Recurring payments are not supported by this plugin version.",
        "Reconcile", "Reconcile payment", "Preserved", "Financial history and encrypted credential profiles are retained when the plugin is uninstalled.",
        "TaskName", "Lunixi payment reconciliation");
    public static readonly IReadOnlyDictionary<string, string> Turkish = Values(
        "Enabled", "Lunixi ödemelerini etkinleştir", "MerchantId", "Merchant kimliği", "KeyId", "Merchant SECRET anahtar kimliği",
        "PrivateKey", "Ed25519 özel anahtar (PKCS#8 PEM)", "WebhookSecret", "Webhook imza sırrı",
        "Hosted", "İsteğe bağlı yönlendirmeli ödeme (backend paymentPageUrl)", "Preauthorize", "Şimdi provizyon al, sonra tahsil et",
        "Methods", "Açık yöntemler, taksit, sigorta ve parçalı ödeme Lunixi panelinizden yönetilir.",
        "SecretHint", "Kayıtlı değerleri korumak için gizli alanları boş bırakın. Sırlar şifrelenir; önceki kimlik profilleri mutabakat ve iadeler için saklanır.",
        "ServerKey", "Her uygulama sunucusunda LUNIXI_NOP_ENCRYPTION_KEY tanımlayın: rastgele 32 bayt içeren, yedeklenmiş base64 anahtar. Veritabanı ve kaynak deposu dışında tutun.",
        "ServerKeyMissing", "Sunucu şifreleme anahtarı eksik. Tanımlanana kadar ödemeler kapalı kalır.",
        "WebhookUrl", "Webhook adresi", "Description", "Lunixi ile güvenli ödeme yapın. Tahsilat mağazanın ana para birimindedir.",
        "Pay", "Güvenli ödemeye devam et", "Pending", "Ödemeniz doğrulanıyor. Bu sayfa tek başına ödeme onayı vermez.",
        "Order", "Siparişi görüntüle", "Unavailable", "Ödeme kullanılamıyor veya mutabakat gerektiriyor. Yeniden denemeden önce sipariş durumunu kontrol edin.",
        "OperationPending", "İşlem bekliyor veya zaten işlendi. Siparişi yenileyip yeniden denemeden önce mutabakat yapın; yeni ödeme oluşturmayın.",
        "InvalidSettings", "Yapılandırma doğrulanamadı. Sunucu şifreleme anahtarını ve Lunixi bilgilerini kontrol edin.",
        "Saved", "Lunixi ayarları kaydedildi.", "UnsupportedRecurring", "Bu eklenti sürümü tekrarlayan ödemeleri desteklemez.",
        "Reconcile", "Ödemeyi uzlaştır", "Preserved", "Eklenti kaldırıldığında finansal geçmiş ve şifrelenmiş kimlik profilleri korunur.",
        "TaskName", "Lunixi ödeme mutabakatı");
    private static IReadOnlyDictionary<string, string> Values(params string[] pairs)
        => Enumerable.Range(0, pairs.Length / 2).ToDictionary(i => Prefix + pairs[i * 2], i => pairs[i * 2 + 1], StringComparer.Ordinal);
}
