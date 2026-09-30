<p align="center"><img src="Assets/supurucu-polished.png" width="112" alt="Süpürücü logosu"></p>
<h1 align="center">Süpürücü · Sweepable’r</h1>
<p align="center"><strong>Windows bakımına daha az zaman. Kendi işine daha çok zaman.</strong></p>
<p align="center">Able’r ağacının ilk dalı · Yerel Windows uygulaması · MIT lisansı</p>
<p align="center">Türkçe · <a href="README.md">English</a></p>
<p align="center"><a href="https://github.com/27-coder/Sweepabler/releases/tag/v1.2.3-dev">Windows x64 indir · Geliştirme ön sürümü</a> · <a href="https://github.com/27-coder/Sweepabler/actions/workflows/build.yml">Derleme kontrolleri</a></p>

## Ortamını kurmak yıllar aldı. Korumak bu kadar uğraştırmasın.

Linux ve macOS'un yönetim araçlarını seviyor olabilirsin. Geçmeyi de düşünmüşsündür. Ama işlerin, oyunların, geliştirme araçların ve alıştığın düzen Windows'ta. Hepsini taşımak başlı başına bir iş.

Süpürücü daha küçük bir soruyla başlıyor: **Windows bilgisayarını düzenli tutmak daha az uğraş istese?**

İngilizce adıyla **Sweepable’r**, uygulama güncellemelerini küçük ve yerel bir masaüstü penceresinde toplar. Eskiyi bul, güncellenecekleri seç, süpürgeyi çalıştır. Resmî kaynaklar, senin seçimin ve yapılan işlemlerin kaydı. Hesap açman ya da bir web paneli yönetmen gerekmez.

Able’r ailesinin ilk ürünü; zaten kullandığın ortamı daha rahat kullanmanı sağlayacak araçların ilk dalı.

## Bir süpürge, farklı güncelleme kaynakları

Her Windows uygulaması aynı şekilde güncellenmez. Süpürücü, doğrulanmış kaynaklardan oluşan gömülü kataloğu **WinGet, Chocolatey ve Scoop** ile birleştirir. İlk hazırlık eksik araçları ve Scoop'un kullandığı Git'i kurar; dördünün de çalıştığını kontrol eder.

- **Yeni bilgisayara hazırlık:** İngilizce dil seçiminde Turkish veya English seçilir. Yerel veri klasörleri hazırlanır, gerekli araçlar kurulur ve kaynakları yenilenir. Hazırlık bitince uygulama kendiliğinden açılır. Bir adım başarısızsa hata ve yeniden deneme düğmesi görünür.
- **Daha hızlı tarama:** Katalog kaynakları aynı anda kontrol edilir; paket yöneticisi taraması da bu sırada ilerler. Başarılı araç bakımı 30 dakika boyunca yeniden kullanılabilir.
- **Küçük işler önce:** Boyutu bilinen küçük yükleyiciler önce indirmeye başlar. En fazla üç doğrudan indirme birlikte yürür. Hazır bir uygulama kurulurken diğerleri indirilebilir.
- **Ağa biraz nefes:** 100 MiB ve üzerindeki doğrudan indirmeler, küçük veya boyutu bilinmeyen indirmeler bitene kadar yaklaşık 1 MiB/sn ile sınırlandırılır. Sonra sınır otomatik kalkar.
- **Karar sende:** Güncellenecekleri seçebilir, **Korsan-aslanı!** ile bazı uygulamaları güncelleme dışında tutabilir ve açık uygulamaların kapatılıp kapatılmayacağına karar verebilirsin. Evet dediğinde uygulama ve alt işlemleri zorla kapatılır; önce çalışmalarını kaydet.
- **Uygulama aileleri birlikte:** Python, Anaconda ve Miniconda gibi ilişkili sonuçlar **Pythongiller** başlığı altında görünür. Her uygulamanın adı, sürümü, kaynağı ve seçimi ayrı kalır. Başlık en az iki ilişkili sonuç varsa görünür.
- **Doğrulanmış indirmeyi yeniden kullan:** Önbellekteki yükleyici yalnızca güncel yayıncı sağlamasıyla eşleşirse yeniden kullanılır. Dosya değişmişse tekrar indirilir.
- **Sonuç görünür:** Geçmiş, doğrulama sonuçları ve güven uyarıları bilgisayarında saklanır. Aynı sürüm çifti için tekrarlayan hatalar geçici engel alır; engeller sıfırlanabilir.
- **Masaüstü düzenli:** Başarılı güncellemenin yeni oluşturduğu, o uygulamaya ait kısayollar kaldırılır. Önceden var olanlar korunur.

Kurulumlar sırayla çalışır. WinGet, Chocolatey ve Scoop indirmelerini kendileri yönetir; doğrudan indirme sınırı onlara uygulanmaz. Boyutu bilinmeyen indirmelerin sırası kesin olarak belirlenemez. Discord yeniden açılma davranışı nedeniyle kapsam dışıdır.

Bir de küçük süpürge yardımcısı var. Çalışır, söylenir, bazen bir yükleyiciye yenilir. Taşıyabilir, boyutunu değiştirebilir veya kapatabilirsin; güncelleme işi devam eder.

## Başlamak

[Windows x64 ZIP](https://github.com/27-coder/Sweepabler/releases/download/v1.2.3-dev/Sweepabler-1.2.3-dev-windows-x64.zip) dosyasını indirip çıkar ve `Süpürücü.exe` dosyasını aç; istersen aşağıdaki komutlarla derle. Taşınabilir sürüm .NET çalışma zamanını ve kataloğu içerir; yanına JSON dosyası gerekmez.

1. `Süpürücü.exe` dosyasını aç ve Windows yönetici isteğini kabul et.
2. **Turkish** veya **English** seç. Hazırlık tamamlanınca uygulama kendiliğinden açılır.
3. **Morukları bul** ile sonuçları incele; seçtiklerin için **Süpür!** düğmesine bas.

Türkçe pencere **Süpürücü**, İngilizce pencere **Sweepable’r** adını kullanır; hazır durum metni **Süpürmeye hazır!** / **Ready to Sweep!** olur. Sonradan dili değiştirmek için alt metne sağ tıkla ve **Language** seç. Dil seçimi her zaman İngilizce görünür; hazırlık seçtiğin dilde devam eder. Ana pencere 430 × 340, hazırlık penceresi 390 × 220 boyutunda kalır; dil seçimi ve hazırlık bu boyutları değiştirmez. WinGet, Git, Chocolatey ve Scoop zorunludur.

**Korsan-aslanı!** yanındaki **Süpürücü’yü süpür**, geliştirme ön sürümleri dahil [bu deponun sürümlerini](https://github.com/27-coder/Sweepabler/releases) kontrol eder; daha yeni EXE'yi doğrular, uygulama kapandıktan sonra bu taşınabilir kopyayı değiştirip yeniden açar. Her tıklama sürüm bilgisini GitHub'dan yeniler. **Süpürücü’yü sil** onaydan sonra yalnızca çalışan taşınabilir EXE'yi kaldırır; ayarlar ve kurulu araçlar korunur. Eski ayarlarda kanal boşsa resmî depo otomatik kullanılır. Ayrıntılar [teknik kılavuzda](docs/TECHNICAL.md#self-update-and-removal).

Mevcut aday **1.2.3-dev**, bir [geliştirme ön sürümüdür](https://github.com/27-coder/Sweepabler/releases/tag/v1.2.3-dev). EXE henüz imzalı olmadığı için Windows SmartScreen ve UAC istemleri gösterebilir. Daha geniş yeni-PC testleri ve sonraki adımlar yol haritasında yer alır.

## Süpürge nereye uzanacak?

Uygulama güncellemeleri ilk dal. Daha geniş hedef, ertelenen Windows bakım işlerini daha anlaşılır ve kolay hâle getirmek.

| Yön | Hedef | Durum |
| --- | --- | --- |
| Yedekleme ve geri dönüş | Seçilen veri ve ayarları yedeklemek; anlaşılır geri yükleme | Planlandı |
| Temizlik önizlemesi | Silinecek dosyaları ve kazanılacak alanı önceden göstermek | Planlandı |
| Güvenli silme | Seçilen dosyaları parçalama; depolama türünün sınırlarını açıklamak | Araştırılıyor |
| Windows seçenekleri | Copilot ve Edge için desteklenen, geri alınabilir kontroller | Araştırılıyor |
| Katalog kapsamı | Yayıncı kaynağı doğrulanmış daha fazla uygulama | Devam ediyor |
| Dağıtım | Kurulum paketi, imzalı derlemeler ve yayımlanmış sürüm kanalı | Planlandı; taşınabilir sürümün kendini güncelleme kodu yapılandırılabilir |

Bunlar bugün çalışan özellikler değildir. Süpürücü şu anda desteklenen seçili uygulamaları günceller ve kendi eski indirmelerini temizler. Her Windows sorununu çözdüğünü iddia etmez. Ayrıntılar [yol haritasında](ROADMAP.md).

## Anlaşılır bir uygulama, yerel veri

Süpürücü WPF ve .NET 10 kullanır. Arka plan servisi, zamanlanmış görev, telemetri hattı veya zorunlu bulut hesabı yoktur.

HTTPS, katalog alan adı kuralları ve yönlendirme kontrolleri uygulanır. Kaynağın sağladığı sağlama tutmazsa yükleyici çalıştırılmaz. Authenticode ve yayıncı kimliği ayrıca kontrol edilir. Eksik veya geçersiz imzada mevcut politika görünür uyarıyla devam etmektir. Sırada bekleyen yükleyici çalıştırılmadan hemen önce yeniden doğrulanır.

Veriler `%LOCALAPPDATA%\Supurucu` altında tutulur. Otomatik temizlik yalnızca buradaki `Downloads` klasöründedir; bağlantı ve junction hedeflerine geçmez. Ayrıntılar [teknik kılavuzda](docs/TECHNICAL.md).

## Derlemek ve katkıda bulunmak

Windows ve **.NET 10 SDK** gerekir:

```powershell
dotnet build .\ProperAppUpdater.csproj -c Release
dotnet run --project .\Tests\ProperAppUpdater.Tests.csproj -c Release
dotnet run --project .\ProperAppUpdater.csproj
dotnet publish .\ProperAppUpdater.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
```

EXE `bin\Release\net10.0-windows\win-x64\publish` altında oluşur. Windows CI derlemeyi ve regresyon kontrollerini çalıştırır; uygulama güncellemelerini kurmaz.

Küçük düzeltmeler, çeviriler, hata raporları ve doğrulanmış katalog kayıtları için [katkı kılavuzu](CONTRIBUTING.md) açık. Somut değişiklikler [sürüm günlüğünde](CHANGELOG.md) tutulur.

Projeye ait kaynak kod [MIT lisansıyla](LICENSE) sunulur. Üçüncü taraf uygulama ve yükleyiciler kendi lisanslarını korur.
