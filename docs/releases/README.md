# Store sürümleri: kurallar

## Nereye bakılır

- **`dist/store/`**: her zaman **gönderilecek son sürüm.** Partner Center'a gereken her şey burada: paket, metinler, görseller. Neyin nereye gittiği `dist/store/README.txt` dosyasında yazar.
- **`dist/old_versions/<sürüm>/`**: önceki sürümler. Yeni sürüm hazırlanınca önceki `dist/store` buraya taşınır, hiçbir şey silinmez.
- `dist/` git'e girmez. Kaynaklar depoda: listeleme metinleri `docs/store-listing/`, sürüme özel metinler `docs/releases/<sürüm>.md`, betikler `scripts/`.

`dist/store` tek komutla hazırlanır:

```bash
powershell -ExecutionPolicy Bypass -File scripts/build-store-release.ps1
```

Betik sürümü `Directory.Build.props` dosyasından okur. `dist/store` başka bir sürümse önce onu `dist/old_versions/` klasörüne taşır. Sonra imzasız x64 paketini derler, metinleri kopyalar ve karakter sınırlarını denetler (aşan metinde durur), poster ve kutu resmini üretir, ekran görüntülerini (tr + en) alır. Aynı sürümü yeniden derlemek `dist/store` klasörünü yeniler. Ekran görüntüleri çalışan uygulamayı kapatır, iş bitince açık bırakmaz.

## Her sürümün metinleri: `docs/releases/<sürüm>.md`

Her sürüm **tek bir Markdown dosyasıdır.** Sürüm, `Directory.Build.props` içindeki `Version` değeridir; pakete son hanesi `0` olarak gider. Yapısı:

```
# <sürüm> — <kısa başlık>
Durum: …  (hazırlanıyor / sertifikasyonda / Store'da yayında, tarih, etiket)
- İçerik, paketler: kısa notlar

# Microsoft Store
## What's new
### EN
### TR
## Notes for certification (<n> karakter)

# Site       ← sitede yapılacaklar (gizlilik sayfası vb.), yoksa bölüm yok
# GitHub release ← markdown bloğu, İngilizce, iş akışı bunu kullanır
# Checklist  ← bu sürümde, gönderimden önce, paket testi, gönderimde, bilinen sorunlar
```

Her metin bir `text` kod bloğundadır ve Partner Center'a olduğu gibi yapıştırılır: düz metin, Markdown yok, madde işareti `•`. Başlık adları değiştirilmez, betik blokları onlarla bulur. GitHub release metninde `## What's new` ve `## Notes for certification` başlıkları kullanılmaz.

| Blok | Partner Center'daki yeri | Ne zaman | Sınır |
|---|---|---|---|
| `## What's new` → `### EN` / `### TR` | Store listing → *What's new in this version* | **yalnız güncellemelerde** | 1500 karakter |
| `## Notes for certification` | Submission options → *Notes for certification* | her sürüm | 2000 karakterin altında |

- **What's New** kullanıcıya yazılır: ne değişti, ne kazandı. İç ayrıntı yok. İki dilde içerik aynıdır.
- **Notes for Certification** İngilizcedir ve inceleme ekibine yazılır. Güncellemelerde metnin **en üstünde** bir `Changes in this version:` paragrafı olur: yalnız inceleme ekibini ilgilendiren değişiklikler (yeni izin, ağ bağlantısı, dosya erişimi). Altında sabit açıklama gelir. Bir önceki gönderim reddedildiyse ret nedenine nasıl çözüm bulunduğu da yazılır.

## Her sürümde yapılacaklar

1. Sürümü artır: `Directory.Build.props` → `Version`.
2. `docs/releases/<sürüm>.md` dosyasını yaz (bir öncekini örnek al).
3. Gizlilik politikasını gözden geçir: yeni bir ağ çağrısı veya saklanan veri varsa güncelle ve gönderimden önce yayımla.
4. FFmpeg değiştiyse: `scripts/build-ffmpeg.ps1`, kaynak paketini sitenin `/source/` klasörüne koy, sitedeki bileşen listesini güncelle.
5. `dotnet test` yeşil olmalı.
6. Özellik commit'leri, sonra `Version X.Y.Z` commit'i.
7. `git push origin main` ve `v<sürüm>` etiketi. Etiket sürüm çıkarılırken atılır, Store onayı beklenmez. `.github/workflows/release.yml` testleri çalıştırır, etiketi sürümle karşılaştırır ve `# GitHub release` bloğuyla release açar. Dosya eklenmez, dağıtım Store'dan.
8. `scripts/build-store-release.ps1` → `dist/store`. Ekran görüntülerine göz at.
9. Paketi test kimliğiyle dene: `scripts/test-package.ps1` (aynı kimlik Store sürümünü bozmasın diye `.Test` adıyla kurar), sonra `-Remove` ile kaldır. Sonucu `# Checklist` altına tablo olarak yaz.
10. Partner Center: paketi yükle, metinleri ve görselleri `dist/store/README.txt` dosyasına göre yerleştir. Durum satırını onaydan sonra güncelle.
