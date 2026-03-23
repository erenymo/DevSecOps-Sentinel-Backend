namespace Sentinel.Application.Abstractions
{
    /// <summary>
    /// deps.dev API'sinden lisans bilgisi çekmek için kullanılan istemci arayüzü.
    /// </summary>
    public interface IDepsDevClient
    {
        /// <summary>
        /// Verilen PURL (Package URL) için deps.dev API'sinden lisans isimlerini çeker.
        /// </summary>
        /// <param name="purl">Paketin Package URL'i (örn: pkg:nuget/Newtonsoft.Json@13.0.1)</param>
        /// <param name="cancellationToken">İptal belirteci</param>
        /// <returns>Lisans SPDX id listesi (örn: ["MIT", "Apache-2.0"]). Bulunamazsa boş liste döner.</returns>
        Task<IReadOnlyList<string>> GetLicensesAsync(string purl, CancellationToken cancellationToken = default);
    }
}
