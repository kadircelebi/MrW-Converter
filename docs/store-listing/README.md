# Store listeleme metinleri

Store ID `9MTCKZXRFFXG` · PFN `MrWProject.MrWConverter_1jmj08nda0ave` · Kimlik `MrWProject.MrWConverter` · Yayıncı `MrWProject` · Store sayfası yayından sonra: `https://apps.microsoft.com/detail/9mtckzxrffxg`

Bu klasör Store listelemesinin **kaynağıdır** ve sürümden sürüme değişmez. Her sürümde `scripts/build-store-release.ps1` bu dosyaları `dist/store/listing/<dil>/` klasörüne kopyalar ve sınırları denetler. Partner Center'a oradan yapıştırılır.

## Dosya → Partner Center alanı

Store listings → (Türkçe / English) listeleme:

| Dosya | Alan | Sınır |
|---|---|---|
| `<dil>/description.txt` | Description | 10.000 karakter |
| `<dil>/features.txt` | Product features (her satır bir madde) | en fazla 20 madde, her biri 200 karakter |
| `<dil>/short-description.txt` | Short description | 1000 karakter |
| `<dil>/keywords.txt` | Search terms (her satır bir kelime) | en fazla 7 |
| `<dil>/license-terms.txt` | Additional license terms | 10.000 karakter |
| (Sürüme özel) `docs/releases/<sürüm>.md` → What's new → EN / TR | What's new in this version | 1500 karakter, ilk sürümde yok |

Diğer alanlar (dosyası yok, elle doldurulur):

- **Copyright and trademark info:** `© 2026 MrWProject`
- **Developed by:** `MrWProject`
- **Privacy policy URL:** `https://converter.mrwproject.com/privacy/`
- **Website:** `https://converter.mrwproject.com/`
- **Support contact:** `https://converter.mrwproject.com/contact/`
- **Category:** Utilities & tools

## Gizlilik politikası

`privacy-policy.md` Türkçe ve İngilizcedir ve `https://converter.mrwproject.com/privacy/` adresinde yayımlanır. Uygulama yeni bir ağ çağrısı yaparsa veya yeni bir veri saklarsa önce bu metin, sonra sitedeki sayfa güncellenir. Sitedeki sayfa panelden düzenlenir; ilk taslağı aynı metindir.

## Yaş derecelendirmesi (IARC)

Uygulama dosya dönüştürür: içerik üretmez, sohbet, paylaşım, satın alma veya konum yoktur. İnternet yalnızca kullanıcı onayıyla LibreOffice indirmek için kullanılır. Kullanıcının kendi dosyalarını işlediği, bunların başkalarıyla paylaşılmadığı buna göre yanıtlanır.

## Görseller

Görseller depoda değil, her sürümde yeniden üretilir: `dist/store/listing/<dil>/screenshots/` ve `.../logos/`.

- Ekran görüntüleri: `scripts/capture-store-screenshots.ps1` paketin içeriğini `MRWCONVERTER_LANGUAGE` ile dil dil çalıştırıp UI Automation ile yakalar. Örnek dosyalar `do_not_share/site/samples/` altında, yapay zekâyla üretilmiş fotoğraflardır; gerçek kişi veya marka içermez.
- Poster, kutu resmi ve sahnelerin birleştirilmesi: `scripts/make-listing-images.py`.
