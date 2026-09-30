<p align="center"><img src="src/Sweepabler/Assets/supurucu-polished.png" width="112" alt="Süpürücü logosu"></p>
<h1 align="center">Süpürücü · Sweepable’r</h1>
<p align="center">Windows için uygulama güncelleyici. Able’r ailesinin ilk ürünü.</p>
<p align="center">Türkçe · <a href="README.md">English</a> · <a href="https://github.com/27-coder/Sweepabler/releases/tag/v1.2.3-dev">İndir</a></p>

Çoğumuz Windows’la başladık. Yıllar içinde işlerimiz, oyunlarımız, araçlarımız ve alıştığımız küçük şeylerle kendi düzenimizi kurduk. Linux veya macOS’a geçmek güzel geliyor ama bütün o ortamı taşımak başlı başına bir iş.

Süpürücü, Windows’ta kalanlar için. Eski uygulamaları bulur, seçtiklerini günceller. İngilizce adı **Sweepable’r**.

## Neler yapıyor?

- WinGet, Chocolatey, Scoop ve resmî indirme kaynaklarından oluşan katalog üzerinden güncelleme bulur.
- Doğrudan indirmelerde aynı anda üç yükleyiciye kadar indirir. Küçük indirmeler önce başlar; büyükler, küçük işler bitene kadar yavaşlar. Kurulumlar tek tek yapılır. Paket yöneticileri kendi indirmelerini yönetir.
- Python, Anaconda ve Miniconda gibi ilişkili uygulamaları birlikte gösterir. Sürümleri ve seçimleri ayrı kalır.
- **Korsan-aslanı!** ile seçtiğin uygulamaları güncelleme dışında tutar.
- İndirilen yükleyicileri kontrol eder ve güncelleme geçmişini bilgisayarında saklar.
- **Süpürücü’yü süpür** düğmesiyle bu depodan kendini günceller.

Türkçe ve İngilizce kullanılabilir. Küçük bir süpürge yardımcısı da var; taşıyabilir, boyutunu değiştirebilir veya kapatabilirsin.

## Çalıştırmak

[Windows x64 ZIP](https://github.com/27-coder/Sweepabler/releases/download/v1.2.3-dev/Sweepabler-1.2.3-dev-windows-x64.zip) dosyasını indir, çıkar ve `Süpürücü.exe` dosyasını aç.

İlk açılışta **Turkish** veya **English** seç. Hazırlık, eksik WinGet, Git, Chocolatey ve Scoop araçlarını kurup kontrol eder; ardından uygulama açılır. Dili daha sonra alttaki **Language** menüsünden değiştirebilirsin.

**Morukları bul** ile tara, güncellemek istediklerini seç ve **Süpür!** düğmesine bas. Açık bir uygulamayı zorla kapatmadan önce onay ister; kabul etmeden önce çalışmalarını kaydet. Discord, güncelleme ve yeniden açılma davranışı nedeniyle kapsam dışında.

**Süpürücü’yü süpür**, geliştirme ön sürümleri dahil bu depoda daha yeni EXE arar. İndirmeyi doğrular, uygulamayı kapatıp çalışan kopyayı değiştirir ve yeniden açar. **Süpürücü’yü sil** onaydan sonra o taşınabilir EXE’yi kaldırır; kayıtlı veriler ve kurulu araçlar kalır.

Mevcut sürüm **1.2.3-dev**. Geliştirme devam ediyor. EXE henüz imzalı olmadığı için açarken Windows uyarısı görebilirsin.

## Sırada ne var?

Daha fazla uygulama desteği, yedekleme, temizlik önizlemesi, dosya parçalama ve Copilot ya da Edge gibi Windows bileşenleri için seçenekler. Bunlar henüz uygulamada yok.

## Kaynak ve veriler

Süpürücü WPF ve .NET 10 kullanır. Uygulama kodu [src/Sweepabler](src/Sweepabler) içinde. Ayarlar, geçmiş ve indirilen dosyalar `%LOCALAPPDATA%\Supurucu` altında tutulur. Hesap ya da arka plan servisi yok.

Kaynak kod [MIT lisansıyla](LICENSE) açık. Katkılara da açık.
