# ROL VE GÖREV TANIMI
Sen .NET mimarisi, kurumsal CMS sistemleri ve çoklu ilişkisel veritabanı soyutlama katmanları (Database-Agnostic RDBMS DAL) konusunda uzmanlaşmış Kıdemli bir Yazılım Mimarı ve Refactoring Uzmanısın.
Görevin: Umbraco CMS'in mevcut SQL Server bağımlı ve NPoco mikro-ORM'ini kullanan veri erişim katmanını; PostgreSQL, MySQL, Oracle, Microsoft SQL Server ve SQLite veritabanı motorlarını "yerel (native)" olarak ve tamamen destekleyecek, esnek, yüksek performanslı ve modern bir EF Core mimarisine dönüştürmek için üretime hazır (production-ready) bir mimari şablon ve kod yapısı tasarlamaktır.

# MEVCUT DURUM VE TEKNİK DETAYLAR
1. Umbraco şu anda veri katmanında NPoco kullanıyor ve kod tabanında SQL Server mimarisine optimize edilmiş ham SQL sorguları barındırıyor.
2. Bu durum; PostgreSQL'deki harf duyarlılığı (Case-Sensitivity) ve otomatik snake_case şema gereksinimleri, Oracle'ın veri tipi limitleri ve benzersiz isimlendirme kuralları, MySQL'in (Pomelo) indeksleme farklılıkları nedeniyle diğer veritabanlarında çalışma zamanı (runtime) hatalarına yol açıyor.
3. Hedefimiz, tek bir kod tabanı (Single Codebase) üzerinden, sadece bağlantı dizesi (Connection String) ve sağlayıcı adı değiştirilerek bu 5 veritabanında da sorunsuz çalışan bir altyapı kurmaktır.

# SENDEN İSTENEN DETAYLI ÇIKTILAR (ADIM ADIM)

## Adım 1: Kurumsal Repository ve Unit of Work Pattern (EF Core)
Bana tüm 5 ilişkisel veritabanında da ortak çalışacak, asenkron yapıda (`async/await`), CQRS mimarilerine uyumlu jenerik bir `IRepository<TEntity>` ve `IUnitOfWork` arayüzü ile bunların EF Core tabanlı somut (concrete) sınıflarını tasarla. Bellek yönetimini ve `DbContext` yaşam döngüsünü doğru yönettiğinden emin ol.

## Adım 2: Dinamik Model Oluşturma ve Sağlayıcıya Özel Yapılandırma (Context Tasarımı)
Öyle bir `UmbracoDbContext` sınıfı yaz ki:
- `OnModelCreating` metodunda aktif olan veritabanı sağlayıcısını (`Database.ProviderName`) tespit etsin.
- Eğer aktif sağlayıcı PostgreSQL ise tüm tablo ve kolon isimlerini otomatik olarak `snake_case` formatına dönüştürsün.
- Oracle veya MySQL'e özel şema/indeks kısıtlamaları veya veri tipi eşleştirmelerini (Mapping) dinamik ve koşullu (Conditional Fluent API) olarak uygulasın.

## Adım 3: Ham T-SQL Sorgularının Dönüştürülmesi ve Maksimum Performans
Mevcut Umbraco çekirdeğindeki ham SQL Server bağımlılıklarını yok etmek için:
- NPoco'daki ham dize (string) sorguların yerini alacak ve tüm veritabanlarında aynı çıktıyı üretecek LINQ sorgu stratejisini göster.
- EF Core'un ham hızda NPoco'yu yakalaması ve hatta geçmesi için `AsNoTracking()`, `Compiled Queries` (Derlenmiş Sorgular) ve `Query Splitting` (Sorgu Bölme) yaklaşımlarını içeren kurumsal bir sorgu örneği yaz (Örn: Umbraco Content Node hiyerarşisini çeken bir metot).

## Adım 4: Veritabanı Göçleri (Migrations) ve Ortak Şema Yönetimi
Farklı SQL lehçelerine sahip bu 5 veritabanı için EF Core Migrations mekanizmasını nasıl kurgulamalıyız? 
- Her veritabanı sağlayıcısı için ayrı şema klasörleri mi üretmeliyiz yoksa ortak tek bir migration yapısı kurulabilir mi? En iyi kurumsal pratiği (Best Practice) mimari detaylarıyla açıkla ve yapılandırma adımlarını göster.

## Adım 5: Esnek Veritabanı Seçim Katmanı (Dependency Injection)
`appsettings.json` dosyasından okunan sağlayıcı bilgisine göre (`PostgreSQL`, `MSSQL`, `MySQL`, `Oracle`, `SQLite`) ilgili DbContext paketini (örn: `Npgsql.EntityFrameworkCore.PostgreSQL`, `Pomelo.EntityFrameworkCore.MySql`) dinamik olarak yükleyen ve IoC Container'a kaydeden bir `AddUmbracoFlexibleDataStores()` extension metodu yaz.

Lütfen yanıtını derinlemesine teknik açıklamalar, performans optimizasyon taktikleri ve doğrudan projeye eklenebilecek temiz C# 12+ / .NET 8+ kod blokları ile yapılandır.
