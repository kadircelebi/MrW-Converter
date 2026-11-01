# MrWConverter

Windows için yerel dosya dönüştürücü. Hedef dağıtım kanalı Microsoft Store'dur; kurallar ve kalan işler için [docs/STORE-READINESS.md](docs/STORE-READINESS.md) dosyasına bakın.

Desteklenen formatlar:

- **Görsel:** JPG, PNG, WebP, TIFF, BMP, AVIF
- **Ses:** MP3, M4A, WAV, FLAC, OGG, Opus; ayrıca AIFF ve WMA okunur
- **Video:** MP4, MOV, MKV, WebM, GIF; ayrıca AVI ve WMV okunur

**Araçlar:** Görsellerde arka plan kaldırma (saydam PNG, cihazın dışına çıkmadan), kırpma ve sansürleme. Kırpma ve sansür aynı formatta yeni dosya üretir, EXIF silinir ve asıl dosya değişmez.

**Sürükleme çarkı:** Alttaki ayar açıldığında, herhangi bir pencereden dosya sürüklerken Shift çarkı açar; bir dilime bırakmak işi başlatır. Araçlar dilimi üzerinde kısa süre beklemek çarkı araçlara çevirir, Geri dilimi formatlara döndürür. Çarkın ortasına bırakmak normal Windows işlemini yapar. Listedeki dosyalar için aynı çark Ctrl+Space ile açılır.

## Yapı

| Klasör | İçerik |
| --- | --- |
| `src/MrWConverter.Core` | Arayüzden bağımsız motor: format kataloğu, güvenli çıktı işlemi, dönüştürücüler |
| `src/MrWConverter.App` | WPF arayüzü (.NET 10) |
| `tests/MrWConverter.Core.Tests` | xUnit testleri |
| `packaging` | MSIX bildirimi, mağaza görselleri ve logo. Logo değişince `scripts/make-icons.ps1` çalıştırılır |
| `scripts/package.ps1` | MSIX üreten betik |
| `third_party/ffmpeg` | `setup-ffmpeg.ps1` ile indirilen LGPL FFmpeg; git dışında tutulur |
| `third_party/models` | `setup-models.ps1` ile indirilen arka plan modeli; git dışında tutulur |

## Komutlar

İlk kurulumda FFmpeg'i indirme (sabit sürüm, SHA-256 denetimli, x64 için 73 MB). Bu adım olmadan ses ve video testleri "atlandı" görünür, MSIX de üretilmez:

```bash
powershell -ExecutionPolicy Bypass -File scripts/setup-ffmpeg.ps1
```

Arka plan modelini indirme (213 MB). Bu adım olmadan model testleri "atlandı" görünür, MSIX de üretilmez:

```bash
powershell -ExecutionPolicy Bypass -File scripts/setup-models.ps1
```

Derleme:

```bash
dotnet build MrWConverter.slnx
```

Testler:

```bash
dotnet test MrWConverter.slnx
```

Geliştirme sürümünü çalıştırma:

```bash
dotnet run --project src/MrWConverter.App
```

MSIX üretme (Windows SDK gerekir):

```bash
powershell -ExecutionPolicy Bypass -File scripts/package.ps1
```

Paket kimliğiyle deneme (Geliştirici Modu gerekir):

```bash
powershell -Command "Add-AppxPackage -Register artifacts\package\x64\layout\AppxManifest.xml"
```

Denemeyi kaldırma:

```bash
powershell -Command "Get-AppxPackage MrWConverter.Dev | Remove-AppxPackage"
```

## Yeni format eklemek

1. `src/MrWConverter.Core/Formats/catalog.json` içine formatı ekleyin; arayüzdeki seçenekler buradan oluşur.
2. Formatı işleyen bir `IFileConverter` yazın veya mevcut olanı genişletin, `App.xaml.cs` içinde kaydedin.
3. Gerçek dosyayla test ekleyin.

## Lisans

Henüz lisans seçilmedi. Üçüncü taraf bileşenler [THIRD-PARTY-NOTICES.txt](src/MrWConverter.App/THIRD-PARTY-NOTICES.txt) dosyasında ve uygulamanın Hakkında penceresinde listelenir. FFmpeg için yayından önce yapılacaklar [docs/STORE-READINESS.md](docs/STORE-READINESS.md) dosyasındadır.
