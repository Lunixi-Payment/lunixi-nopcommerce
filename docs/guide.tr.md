# nopCommerce ödeme eklentisi

Mevcut Lunixi formu varsayılan olarak **mağazanın kendi sitesinde açılır**. Yönlendirmeli ödeme isteğe bağlıdır ve yalnız backend’in döndürdüğü doğrulanmış `paymentPageUrl` kullanılır. Adresin opak token’ı gömülü token’dan farklı olabilir; modül adresi token’dan üretmez. Eksik/eski adres gömülü ödemeyi engellemez; yönlendirmeli ödeme yeni intent veya anahtar açmadan inceleme/uzlaştırmada kalır. Mevcut denemelerin kayıtlı modu, gövdesi, profili ve anahtarı korunur. Yöntemler ve çoklu kart mevcut Lunixi formu/panelinden yönetilir.

Lunixi Payments 0.1.2, nopCommerce'in native `IPaymentMethod` sözleşmesini uygular. Hedef **4.90 API ailesidir**; derleme ve native veritabanı doğrulaması **4.90.8** ve .NET 9 ile yapılmıştır. İmza, kimlik doğrulama, ödeme taşıması ve webhook doğrulaması için ortak [.NET SDK](https://docs.lunixi.com/tr/sdks/dotnet) kullanılır.

Kaynak: [Lunixi-Payment/lunixi-nopcommerce](https://github.com/Lunixi-Payment/lunixi-nopcommerce). Kaynağın bulunması, nopCommerce Marketplace veya NuGet.org yayını anlamına gelmez.

## Derleme ve kurulum

Mağazaya tamamlanmış ZIP yüklenir; paket indirme veya geliştirici SDK'sı gerekmez. Kaynaktan derleyen geliştirici için .NET 9 ve Python 3.10+ gerekir:

```bash
python3 scripts/bootstrap.py --sdk-source /absolute/path/to/lunixi-dotnet
python3 scripts/build.py --restore
```

Bootstrap, SHA-256 ile sabitlenmiş resmî nopCommerce 4.90.8 referanslarını alır. Güvenilir mevcut NoSource klasörü `--nop-runtime` ile verilebilir. Ortak SDK'nın varsayılan kaynağı GitHub'daki `lunixi-dotnet` v0.1.0 etiketidir; etiket henüz yoksa `--sdk-source`, açıkça sağlanan yerel `Lunixi.Sdk.0.1.0.nupkg` için `--sdk-package` kullanın. Yayımlanmamış kayıt paketi varmış gibi davranılmaz.

Çıktı: `artifacts/Lunixi.Payments.nopCommerce.4.90-0.1.2.zip` ve SHA-256 dosyası. Arşiv; native `Payments.Lunixi` klasörünü, eklentiyi, görünüm/varlıkları ve özel SDK yükleme bağlamını içerir. Platform DLL'leri eklentiyle yeniden dağıtılmaz.

**Configuration → Local plugins → Upload plugin or theme** üzerinden yükleyin, nopCommerce'in istediği yeniden başlatmayı yapın ve **Lunixi Payments** eklentisini kurun. Ardından **Configuration → Payment methods** altında etkinleştirin.

## Sunucu anahtarı ve merchant ayarları

Ödemeleri açmadan önce rastgele 32 baytlık sunucu anahtarı üretin; base64 karşılığını her uygulama örneğinde `LUNIXI_NOP_ENCRYPTION_KEY` olarak tanımlayın. Veritabanı ve kaynak deposu dışında yedekleyin. Anahtarı kaybetmek veya değiştirmek eski kimlik bilgilerinin çözülmesini engeller.

| Ayar | İşlev |
| --- | --- |
| Merchant kimliği | Doğrulanmış ödeme kaydında beklenen işletme |
| Merchant SECRET anahtar kimliği | Sunucu API anahtarı; TEST/LIVE ortamını belirler |
| Ed25519 PKCS#8 PEM özel anahtar | İmza; yalnız sunucuda kalır |
| Webhook imza sırrı | Ham gövdeden v2 doğrulaması |
| pay.lunixi.com adresine yönlendir | Açık: yönlendirmeli; kapalı: gömülü ortak form |
| Şimdi provizyon al, sonra tahsil et | Native capture işlemine kadar provizyon |

Gizli alanlar ayarlar gösterilirken boştur; korumak için boş bırakın. Sırlar ve tarayıcı token'ları şifrelenir. Kimlik bilgisi değişirse eski ödemelerin mutabakat/iadesi için değişmez profil oluşturulur. Yalnız görünüm/provizyon seçeneğini değiştirmek yeni kimlik profili oluşturmaz. Mevcut denemeler ilk istek gövdesini ve gösterim biçimini korur.

Gateway kökü `https://api-gateway.lunixi.com` olarak sabittir; TEST/LIVE anahtardan gelir. Önce staging mağazada TEST anahtarı kullanın. → [Kimlik doğrulama](https://docs.lunixi.com/tr/getting-started/authentication), [Test](https://docs.lunixi.com/tr/getting-started/testing)

## Yönlendirmeli ve gömülü form

Yönlendirmeli biçim alıcıyı `paymentPageUrl` adresine götürür. Gömülü biçim, sabit resmî `https://cdn.lunixi.com/lunixi-checkout.min.js` paketini yükleyerek mevcut formu açar. Eklenti kart alanlarını yeniden yazmaz, gateway HTML'i basmaz ve ham kart verisi toplamaz.

Açık yöntemler, taksit, sigorta ve parçalı ödeme uygunluğu **Lunixi panelinden** yönetilir. Eklentide aynı yöntemler için ikinci bir filtre yoktur. Alıcı/adres ön verisi güvenilir native siparişten gelir; eksik isteğe bağlı adres uydurulmak yerine gönderilmez. Siparişin müşteri dili forma Türkçe veya İngilizce aktarılır.

HTTPS ve güvenilir proxy başlıklarını yapılandırın. Gömülü biçimde resmî SDK'yı ve gereken ağ/frame köklerini CSP'de açın. Token gösterilmeden müşteri ve mağaza sahipliği denetlenir. Tarayıcı dönüşü yalnız doğrulamanın beklendiğini gösterir.

## Webhook ve mutabakat

Gösterilen **HTTPS webhook adresini**, doğru ortam ve ödeme olay abonelikleriyle Lunixi paneline kaydedin. Biçim `https://shop.example/lunixi/webhook/<profile-id>` şeklindedir. İade/mutabakat saklama süresince önceki endpoint ve kimlik profillerini koruyun. Yenilenen endpoint sırrı aynı merchant'ın eski imza profiline ait olayı alabilir; tekillik bağlı ödeme denemesine göre yürür.

Alıcı; ham baytları, imza rotasyonunu, imzalı olay/zaman bağını ve güncelliği doğrular. Yalnız yerelde bilinen merchant ödemesini bulur; ardından kimliği doğrulanmış sorguda merchant, ödeme, sipariş, para birimi, tutar ve tam tahsilat bakiyelerini eşleştirir. İmza veya HTTP 200 tek başına siparişi ödenmiş yapamaz. Native sipariş ve inbox, sipariş satırı kilidiyle aynı veritabanı işleminde tamamlanır; kopya/çakışma kaydı kalıcıdır. → [Webhook'lar](https://docs.lunixi.com/tr/getting-started/webhooks)

Zamanlanmış görev yeni siparişleri aç bırakmadan salt okunur mutabakat yapar. Belirsiz sonuç ilk anahtarı korur. Yeni deneme ancak tahsilat olmayan, kimliği doğrulanmış kesin başarısızlık sonrası açılır; token süresinin bitmesi yeni çekim izni değildir.

## Native çekim, iade ve tahsilat

Native sipariş ödeme eylemlerini kullanın. Capture/refund/void, sipariş düzenleme yetkisi ister; native admin CSRF kapıları korunur. İşlem gateway çağrısından önce kaydedilir ve sabit anahtar kullanır. Native sonuç başarılı dönmeden istenen etki tam, doğrulanmış tahsilat bakiyesiyle kanıtlanmalıdır.

| Gateway kanıtı | Native davranış |
| --- | --- |
| Tam çekim, eşleşen kimlik ve bakiyeler | Native sipariş akışıyla ödenmiş |
| Çekimsiz provizyon | Authorized/beklemede |
| Doğrulanmış kısmi/tam iade | Native iade durumu ve bakiyesi |
| Bekleyen, kısmi çekim, belirsizlik veya mutabakat | Bekleyen/inceleme; teslimat yok |
| Dengeli, tam çekilmiş ve telafisiz parçalı ödeme ana kaydı | Ana sipariş tamamlanabilir |
| Çekilmiş tek bacak veya yalnız senet düzenlenmesi | Otomatik teslimat izni vermez |

Tahsilat mağazanın **ana para biriminde**, tam sayı alt birimle yapılır. Sepet tabanının üzerindeki onaylı fark için kimliği doğrulanmış metadata'da kalıcı sipariş parmak izi gerekir. Brüt çekim/iade ayrıca saklanır; fark sepete ikinci kez eklenmez. Son native sepet iadesi kalan doğrulanmış taksit farkını da kapsar. Ürüne özgü sigorta/teslimat politikası, ilgili merchant/form yeteneğini ve uygun sipariş bağlamını gerektirir.

> Kaynakta parçalı ödeme bulunması gateway dağıtımı veya merchant aktivasyonu anlamına gelmez. Bekleyen/mutabakat durumunda teslimat yapılmaz. Senet düzenlenmesi nakit tahsilat değildir; açık merchant teslimat politikası gerekir.

nopCommerce ödeme metodu döndükten sonra native finansal sonucu yazar. Eklenti bu devri rezerve eder, webhook yarışını engeller, native sipariş olaylarını ve yanıtın bitişini izler. Bu kısa devir sırasında süreç çökmesi operatör incelemesi gerektirebilir; kayıtlı işlem kör tekrarı engeller. Çözmeden önce native durumu ve doğrulanmış gateway kaydını kontrol edin.

## Saklama ve uyumluluk sınırları

Kaldırma; görevi kapatır, finansal deneme/işlem/inbox/ayar ve şifreli profilleri korur. Sunucu anahtarını ve gereken merchant bilgilerini iade/itiraz süresi boyunca saklayın. 0.1.2'de tekrarlayan ödeme yoktur. Eski nopCommerce aileleri ve 5.00 beta için uyumluluk iddiası yoktur.

Native kabul testleri gerçek MariaDB 11/InnoDB ve resmî mapping, repository, `OrderService`, `OrderProcessingService` ile çalışır. Ambient transaction katılımı zorunludur; MySQL finans/sipariş/not/e-posta kuyruğu tabloları InnoDB olmalı ve denetlenir. SQL Server/PostgreSQL yolları uygulanmıştır ancak yerel native kabul testleri bu sağlayıcılarda çalıştırılmamıştır; staging doğrulaması yapın. Üçüncü taraf olay tüketicileri tamamlanmamış işlemden dış etki üretmek yerine kalıcı kuyruk kullanmalıdır.

Depodaki doğrulama kaydı gerçek komutları ve sınırları listeler. Ortak SDK, özel assembly/yerel kripto yükleme, native yaşam döngüsü, yarış/tekrar, rollback, şifreli saklama, yetki, tutar/fark/parçalı ödeme ve belirsiz sonuç testleri vardır. Canlı ödeme yapılmaz; kaynak yayını marketplace onayı anlamına gelmez.

`FAILED` durumu, sıfır yakalanan tutarla birlikte bile tek başına kesin başarısızlık kanıtı değildir. Otomatik yeni anahtar oluşturulmaz; inceleme gerekir. Yeni deneme yalnızca doğrulanmış `VOIDED`/`EXPIRED` ve sıfır tahsilatla açılabilir.

Standart tarayıcı dönüşü POST kullanır. Sınırlı ve anonim alıcı, gönderilen alanların tamamını yok sayıp yalnızca aynı mağaza kökünde sabit 303 yönlendirmesi yapar. Ardından gelen HTTPS GET, sipariş bağlantısını göstermeden önce yerleşik müşteri/mağaza sahipliğini yeniden denetler. Dönüş alanları ödeme kanıtı sayılmaz ve bu iki adım gateway uzlaştırması yapmaz.
