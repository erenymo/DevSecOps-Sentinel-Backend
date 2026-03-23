using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Sentinel.Application.Abstractions;
using System.Net;
using System.Text.Json;

namespace Sentinel.Infrastructure.DepsDev
{
    /// <summary>
    /// deps.dev API v3 istemcisi. PURL üzerinden lisans bilgilerini çeker.
    /// HttpClientFactory + Polly (retry/circuit breaker) ile dayanıklıdır.
    /// IMemoryCache ile performans optimize edilir.
    /// </summary>
    public class DepsDevClient : IDepsDevClient
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly ILogger<DepsDevClient> _logger;

        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

        public DepsDevClient(
            HttpClient httpClient,
            IMemoryCache cache,
            ILogger<DepsDevClient> logger)
        {
            _httpClient = httpClient;
            _cache = cache;
            _logger = logger;
        }

        public async Task<IReadOnlyList<string>> GetLicensesAsync(string purl, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(purl))
                return Array.Empty<string>();

            // 1. Cache kontrolü
            var cacheKey = $"depsdev:license:{purl}";
            if (_cache.TryGetValue(cacheKey, out IReadOnlyList<string>? cachedLicenses) && cachedLicenses != null)
            {
                _logger.LogDebug("Cache hit for {Purl}", purl);
                return cachedLicenses;
            }

            // 2. PURL'den sistem ve paket bilgisini çıkart
            if (!TryParsePurl(purl, out var system, out var packageName, out var version))
            {
                _logger.LogWarning("Geçersiz PURL formatı: {Purl}", purl);
                return Array.Empty<string>();
            }

            try
            {
                // 3. deps.dev API v3 çağrısı
                // Endpoint: GET /v3/systems/{system}/packages/{package}/versions/{version}
                var encodedPackage = Uri.EscapeDataString(packageName);
                var encodedVersion = Uri.EscapeDataString(version);
                var requestUri = $"v3/systems/{system}/packages/{encodedPackage}/versions/{encodedVersion}";

                var response = await _httpClient.GetAsync(requestUri, cancellationToken);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("Paket bulunamadı: {Purl}", purl);
                    var empty = Array.Empty<string>();
                    _cache.Set(cacheKey, (IReadOnlyList<string>)empty, TimeSpan.FromHours(1));
                    return empty;
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var licenses = ParseLicensesFromJson(json);

                // 4. Sonucu cache'e yaz
                _cache.Set(cacheKey, licenses, CacheDuration);

                _logger.LogInformation("deps.dev'den lisans çekildi: {Purl} → [{Licenses}]", purl, string.Join(", ", licenses));
                return licenses;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("deps.dev rate limit: {Purl}. Polly retry devreye girecek.", purl);
                throw; // Polly retry'a bırak
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "deps.dev API hatası: {Purl}", purl);
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// PURL'yi (Package URL) ecosystem, package name ve version olarak ayrıştırır.
        /// Örn: "pkg:nuget/Newtonsoft.Json@13.0.1" → ("nuget", "Newtonsoft.Json", "13.0.1")
        /// </summary>
        private static bool TryParsePurl(string purl, out string system, out string packageName, out string version)
        {
            system = string.Empty;
            packageName = string.Empty;
            version = string.Empty;

            try
            {
                // Format: pkg:<type>/<namespace>/<name>@<version> veya pkg:<type>/<name>@<version>
                if (!purl.StartsWith("pkg:", StringComparison.OrdinalIgnoreCase))
                    return false;

                var withoutScheme = purl.Substring(4); // "nuget/Newtonsoft.Json@13.0.1"

                var slashIndex = withoutScheme.IndexOf('/');
                if (slashIndex < 0) return false;

                var rawType = withoutScheme.Substring(0, slashIndex); // "nuget"
                var remainder = withoutScheme.Substring(slashIndex + 1); // "Newtonsoft.Json@13.0.1"

                var atIndex = remainder.LastIndexOf('@');
                if (atIndex < 0) return false;

                packageName = remainder.Substring(0, atIndex);
                version = remainder.Substring(atIndex + 1);

                // Qualifiers (?) varsa version'dan temizle
                var qualifierIndex = version.IndexOf('?');
                if (qualifierIndex >= 0)
                    version = version.Substring(0, qualifierIndex);

                // deps.dev API system adları: npm, go, maven, pypi, cargo, nuget
                system = rawType.ToLowerInvariant() switch
                {
                    "npm" => "npm",
                    "nuget" => "nuget",
                    "pypi" => "pypi",
                    "golang" or "go" => "go",
                    "maven" => "maven",
                    "cargo" => "cargo",
                    _ => rawType.ToLowerInvariant()
                };

                return !string.IsNullOrEmpty(system) && !string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(version);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// deps.dev API JSON yanıtından lisans bilgilerini çıkarır.
        /// </summary>
        private static IReadOnlyList<string> ParseLicensesFromJson(string json)
        {
            var licenses = new List<string>();

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // deps.dev v3 yanıt formatı: { "licenses": ["MIT", "Apache-2.0"] }
                if (root.TryGetProperty("licenses", out var licensesElement))
                {
                    foreach (var item in licensesElement.EnumerateArray())
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            licenses.Add(value);
                    }
                }
            }
            catch (JsonException)
            {
                // Geçersiz JSON
            }

            return licenses.AsReadOnly();
        }
    }
}
