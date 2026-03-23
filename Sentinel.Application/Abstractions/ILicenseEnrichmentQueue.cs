namespace Sentinel.Application.Abstractions
{
    /// <summary>
    /// Lisans zenginleştirme (enrichment) işlemleri için in-memory kuyruk arayüzü.
    /// Tarama tamamlandığında bileşen ID'leri bu kuyruğa eklenir,
    /// arka plan servisi tarafından tüketilir.
    /// </summary>
    public interface ILicenseEnrichmentQueue
    {
        /// <summary>
        /// Bir Scan ID'yi kuyruğa ekler. Bu scan'deki tüm bileşenler işlenecek.
        /// </summary>
        ValueTask EnqueueScanAsync(Guid scanId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Kuyruktan bir Scan ID çeker. Kuyruk boşsa bekler.
        /// </summary>
        ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
    }
}
