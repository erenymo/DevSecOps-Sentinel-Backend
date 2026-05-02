using Microsoft.Extensions.Logging;
using Sentinel.Application.Abstractions;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.Infrastructure.Osv
{
    public class OsvClient : IOsvClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<OsvClient> _logger;

        public OsvClient(HttpClient httpClient, ILogger<OsvClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<IReadOnlyList<OsvVulnerabilityDto>> GetVulnerabilitiesAsync(string purl, string version, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(purl) || string.IsNullOrWhiteSpace(version))
                return Array.Empty<OsvVulnerabilityDto>();

            try
            {
                var cleanPurl = purl;
                var atIndex = cleanPurl.LastIndexOf('@');
                if (atIndex > 0)
                {
                    cleanPurl = cleanPurl.Substring(0, atIndex);
                }

                var requestBody = new
                {
                    package = new { purl = cleanPurl },
                    version = version
                };

                var response = await _httpClient.PostAsJsonAsync("v1/query", requestBody, cancellationToken);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = ParseVulnerabilitiesFromJson(json);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OSV API error for {Purl} {Version}", purl, version);
                return Array.Empty<OsvVulnerabilityDto>();
            }
        }

        private static IReadOnlyList<OsvVulnerabilityDto> ParseVulnerabilitiesFromJson(string json)
        {
            var vulnerabilities = new List<OsvVulnerabilityDto>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("vulns", out var vulnsElement))
                {
                    foreach (var vuln in vulnsElement.EnumerateArray())
                    {
                        var id = vuln.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                        var summary = vuln.TryGetProperty("summary", out var sumProp) ? sumProp.GetString() ?? "" : "";
                        var details = vuln.TryGetProperty("details", out var detProp) ? detProp.GetString() ?? "" : "";
                        
                        string? alias = null;
                        if (vuln.TryGetProperty("aliases", out var aliasesArray))
                        {
                            foreach (var a in aliasesArray.EnumerateArray())
                            {
                                var aStr = a.GetString();
                                if (aStr != null && aStr.StartsWith("CVE-", StringComparison.OrdinalIgnoreCase))
                                {
                                    alias = aStr;
                                    break;
                                }
                            }
                        }

                        string? severityType = null;
                        decimal? severityScore = null;
                        string? severityLevel = null;
                        string? fixedVersion = null;
                        
                        if (vuln.TryGetProperty("severity", out var severityArray))
                        {
                            foreach (var sev in severityArray.EnumerateArray())
                            {
                                if (sev.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "CVSS_V3")
                                {
                                    severityType = "CVSS_V3";
                                    var vector = sev.TryGetProperty("score", out var scoreProp) ? scoreProp.GetString() : null;
                                    if (!string.IsNullOrEmpty(vector))
                                    {
                                        var cvssResult = CvssCalculator.CalculateCvssV3(vector);
                                        severityScore = cvssResult.Score;
                                        severityLevel = cvssResult.Level;
                                    }
                                }
                            }
                        }

                        if (vuln.TryGetProperty("affected", out var affectedArray))
                        {
                            foreach (var affected in affectedArray.EnumerateArray())
                            {
                                if (affected.TryGetProperty("ranges", out var rangesArray))
                                {
                                    foreach (var range in rangesArray.EnumerateArray())
                                    {
                                        if (range.TryGetProperty("events", out var eventsArray))
                                        {
                                            foreach (var evt in eventsArray.EnumerateArray())
                                            {
                                                if (evt.TryGetProperty("fixed", out var fixedProp))
                                                {
                                                    fixedVersion = fixedProp.GetString();
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        vulnerabilities.Add(new OsvVulnerabilityDto(id, alias, summary, details, severityType, severityScore, severityLevel, fixedVersion, vuln.GetRawText()));
                    }
                }
            }
            catch (JsonException)
            {
            }
            return vulnerabilities;
        }
    }
}
