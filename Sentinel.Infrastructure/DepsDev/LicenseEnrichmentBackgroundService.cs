using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sentinel.Application.Abstractions;
using Sentinel.Domain.Entities;
using System.Collections.Concurrent;

namespace Sentinel.Infrastructure.DepsDev
{
    /// <summary>
    /// Arka plan servisi: Kuyruktan Scan ID alır, bileşenlerin PURL'leri ile
    /// deps.dev'den lisans bilgisi çeker ve veritabanına yazar.
    /// </summary>
    public class LicenseEnrichmentBackgroundService : BackgroundService
    {
        private readonly ILicenseEnrichmentQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<LicenseEnrichmentBackgroundService> _logger;

        // Aynı anda kaç paket için paralel istek atacağımız
        private const int MaxDegreeOfParallelism = 5;

        // Her istek arasında bekleme (rate-limiting koruması)
        private static readonly TimeSpan RequestDelay = TimeSpan.FromMilliseconds(200);

        public LicenseEnrichmentBackgroundService(
            ILicenseEnrichmentQueue queue,
            IServiceScopeFactory scopeFactory,
            ILogger<LicenseEnrichmentBackgroundService> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("LicenseEnrichmentBackgroundService başlatıldı.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var scanId = await _queue.DequeueAsync(stoppingToken);
                    _logger.LogInformation("Lisans zenginleştirme başlıyor: ScanId={ScanId}", scanId);

                    await ProcessScanAsync(scanId, stoppingToken);

                    _logger.LogInformation("Lisans zenginleştirme tamamlandı: ScanId={ScanId}", scanId);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Uygulama kapanıyor, normal çıkış
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lisans zenginleştirme sırasında beklenmeyen hata.");
                    // Hata olsa bile devam et, servisi çökertme
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }

            _logger.LogInformation("LicenseEnrichmentBackgroundService durduruluyor.");
        }

        /// <summary>
        /// Lisans API çağrı sonuçlarını taşıyan DTO.
        /// API çağrıları paralel yapılır, sonuçlar bu yapıda toplanır.
        /// </summary>
        private record LicenseFetchResult(string Purl, List<string> LicenseNames);

        private async Task ProcessScanAsync(Guid scanId, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var depsDevClient = scope.ServiceProvider.GetRequiredService<IDepsDevClient>();

            // 1. Scan'deki tüm bileşenleri çek
            var components = await unitOfWork.Components.GetAllAsync(
                c => c.ScanId == scanId);

            var componentList = components.ToList();

            _logger.LogInformation("ScanId={ScanId}: {Count} bileşen işlenecek.", scanId, componentList.Count);

            // 2. Sadece PURL'ü olanları al
            var purls = componentList
                .Where(c => !string.IsNullOrWhiteSpace(c.Purl))
                .Select(c => c.Purl!)
                .Distinct()
                .ToList();

            // 3. Veritabanında bu PURL'lere ait lisanslar var mı kontrol et
            var existingPackageLicenses = await unitOfWork.PackageLicenses.GetAllAsync(
                pl => purls.Contains(pl.Purl));

            var processedPurls = existingPackageLicenses.Select(pl => pl.Purl).Distinct().ToHashSet();

            // Henüz lisansı olmayan PURL'leri bul
            var toProcessPurls = purls.Where(p => !processedPurls.Contains(p)).ToList();

            _logger.LogInformation("ScanId={ScanId}: {Count} farklı PURL lisans zenginleştirmesi bekliyor.", scanId, toProcessPurls.Count);

            // ═══════════════════════════════════════════════════════════════════
            // AŞAMA 1: API çağrılarını paralel yap, sonuçları thread-safe koleksiyonda topla
            // (DbContext'e bu aşamada DOKUNMA — thread-safe değil!)
            // ═══════════════════════════════════════════════════════════════════
            var fetchResults = new ConcurrentBag<LicenseFetchResult>();

            using var semaphore = new SemaphoreSlim(MaxDegreeOfParallelism);
            var tasks = toProcessPurls.Select(async purl =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var licenseNames = await depsDevClient.GetLicensesAsync(purl, cancellationToken);

                    if (licenseNames.Any())
                    {
                        fetchResults.Add(new LicenseFetchResult(purl, licenseNames.ToList()));
                    }
                    else
                    {
                        _logger.LogDebug("Lisans bulunamadı: {Purl}", purl);
                    }

                    await Task.Delay(RequestDelay, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PURL lisans zenginleştirme API hatası: {Purl}", purl);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            // ═══════════════════════════════════════════════════════════════════
            // AŞAMA 2: Sonuçları SIRAYLA (tek thread) DbContext'e yaz
            // Bu sayede concurrent DbContext erişimi ve Dictionary corruption hatası önlenir.
            // ═══════════════════════════════════════════════════════════════════

            // Mevcut lisansları DB'den çekip in-memory cache'e al (deduplication)
            var existingLicenses = await unitOfWork.Licenses.GetAllAsync();
            var licenseCache = existingLicenses
                .ToDictionary(l => l.Name, l => l, StringComparer.OrdinalIgnoreCase);

            foreach (var result in fetchResults)
            {
                foreach (var licenseName in result.LicenseNames)
                {
                    // Cache'den bak veya yeni oluştur (tek thread — güvenli)
                    if (!licenseCache.TryGetValue(licenseName, out var license))
                    {
                        license = new License
                        {
                            Name = licenseName,
                            Type = ClassifyLicenseType(licenseName),
                            RiskLevel = ClassifyRiskLevel(licenseName)
                        };
                        await unitOfWork.Licenses.AddAsync(license);
                        licenseCache[licenseName] = license; // Cache'e ekle → duplicate önle
                    }

                    // PackageLicense ilişkisini oluştur
                    var packageLicense = new PackageLicense
                    {
                        Purl = result.Purl,
                        LicenseId = license.Id
                    };
                    await unitOfWork.PackageLicenses.AddAsync(packageLicense);
                }

                _logger.LogDebug("Lisans eşleştirildi: {Purl} → [{Licenses}]",
                    result.Purl, string.Join(", ", result.LicenseNames));
            }

            // 6. Tarama durumunu güncelle
            var scan = await unitOfWork.Scans.GetByIdAsync(scanId);
            if (scan != null)
            {
                scan.LicenseEnrichmentCompleted = true;
                unitOfWork.Scans.Update(scan);
            }

            // 7. Tüm değişiklikleri tek seferde kaydet
            await unitOfWork.SaveChangesAsync();
        }

        /// <summary>
        /// Lisans adına göre temel risk seviyesi sınıflandırması.
        /// </summary>
        private static string ClassifyRiskLevel(string licenseName)
        {
            var upper = licenseName.ToUpperInvariant();

            // Copyleft lisanslar (yüksek risk)
            if (upper.Contains("GPL") && !upper.Contains("LGPL"))
                return "High";

            // Zayıf copyleft (orta risk)
            if (upper.Contains("LGPL") || upper.Contains("MPL") || upper.Contains("EPL") || upper.Contains("CDDL"))
                return "Medium";

            // Permissive lisanslar (düşük risk)
            return "Low";
        }

        /// <summary>
        /// Lisans türü sınıflandırması.
        /// </summary>
        private static string ClassifyLicenseType(string licenseName)
        {
            var upper = licenseName.ToUpperInvariant();

            if (upper.Contains("GPL") && !upper.Contains("LGPL"))
                return "Copyleft";
            if (upper.Contains("LGPL") || upper.Contains("MPL") || upper.Contains("EPL"))
                return "Weak Copyleft";
            if (upper.Contains("MIT") || upper.Contains("APACHE") || upper.Contains("BSD") || upper.Contains("ISC"))
                return "Permissive";

            return "Other";
        }
    }
}
