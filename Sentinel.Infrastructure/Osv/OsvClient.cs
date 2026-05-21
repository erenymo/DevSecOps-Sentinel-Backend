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
                var result = ParseVulnerabilitiesFromJson(json, version, cleanPurl);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OSV API error for {Purl} {Version}", purl, version);
                return Array.Empty<OsvVulnerabilityDto>();
            }
        }

        private static IReadOnlyList<OsvVulnerabilityDto> ParseVulnerabilitiesFromJson(string json, string currentVersion, string targetPurl)
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

                        if (vuln.TryGetProperty("severity", out var severityArray) && severityArray.ValueKind == JsonValueKind.Array)
                        {
                            JsonElement? selectedSeverity = null;
                            string selectedType = "";

                            foreach (var sev in severityArray.EnumerateArray())
                            {
                                if (sev.TryGetProperty("type", out var typeProp))
                                {
                                    var typeStr = typeProp.GetString();
                                    if (typeStr == "CVSS_V4")
                                    {
                                        selectedSeverity = sev;
                                        selectedType = "CVSS_V4";
                                        break; // Best option found
                                    }
                                    else if (typeStr == "CVSS_V3" && selectedType != "CVSS_V4")
                                    {
                                        selectedSeverity = sev;
                                        selectedType = "CVSS_V3";
                                    }
                                    else if (typeStr == "CVSS_V2" && selectedType != "CVSS_V4" && selectedType != "CVSS_V3")
                                    {
                                        selectedSeverity = sev;
                                        selectedType = "CVSS_V2";
                                    }
                                }
                            }

                            if (selectedSeverity != null)
                            {
                                severityType = selectedType;
                                var vector = selectedSeverity.Value.TryGetProperty("score", out var scoreProp) ? scoreProp.GetString() : null;
                                if (!string.IsNullOrEmpty(vector))
                                {
                                    if (selectedType == "CVSS_V4")
                                    {
                                        var cvssResult = CvssCalculator.CalculateCvssV4(vector);
                                        severityScore = cvssResult.Score;
                                        severityLevel = cvssResult.Level;
                                    }
                                    else if (selectedType == "CVSS_V3")
                                    {
                                        var cvssResult = CvssCalculator.CalculateCvssV3(vector);
                                        severityScore = cvssResult.Score;
                                        severityLevel = cvssResult.Level;
                                    }
                                    else if (selectedType == "CVSS_V2")
                                    {
                                        var cvssResult = CvssCalculator.CalculateCvssV2(vector);
                                        severityScore = cvssResult.Score;
                                        severityLevel = cvssResult.Level;
                                    }
                                }
                            }
                        }

                        string? targetFixedVersion = null;
                        string? fallbackFixedVersion = null;
                        bool foundCorrectBlock = false;

                        if (vuln.TryGetProperty("affected", out var affectedArray) && affectedArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var affected in affectedArray.EnumerateArray())
                            {
                                if (PackageMatches(affected, targetPurl))
                                {
                                    bool versionInList = false;
                                    if (affected.TryGetProperty("versions", out var versionsArray) && versionsArray.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var v in versionsArray.EnumerateArray())
                                        {
                                            var vStr = v.GetString();
                                            if (string.Equals(vStr, currentVersion, StringComparison.OrdinalIgnoreCase) ||
                                                (vStr != null && string.Equals(CleanVersion(vStr), CleanVersion(currentVersion), StringComparison.OrdinalIgnoreCase)))
                                            {
                                                versionInList = true;
                                                break;
                                            }
                                        }
                                    }

                                    string? matchedFixedVersion = null;
                                    bool versionInRange = false;

                                    if (affected.TryGetProperty("ranges", out var rangesArray) && rangesArray.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var range in rangesArray.EnumerateArray())
                                        {
                                            if (IsVersionInRange(currentVersion, range))
                                            {
                                                versionInRange = true;
                                                if (range.TryGetProperty("events", out var eventsArray) && eventsArray.ValueKind == JsonValueKind.Array)
                                                {
                                                    foreach (var evt in eventsArray.EnumerateArray())
                                                    {
                                                        if (evt.TryGetProperty("fixed", out var fixedProp))
                                                        {
                                                            matchedFixedVersion = fixedProp.GetString();
                                                        }
                                                    }
                                                }
                                                break;
                                            }
                                        }
                                    }

                                    if (versionInList || versionInRange)
                                    {
                                        if (matchedFixedVersion != null)
                                        {
                                            targetFixedVersion = matchedFixedVersion;
                                        }
                                        else
                                        {
                                            targetFixedVersion = ExtractAnyFixedVersion(affected);
                                        }
                                        foundCorrectBlock = true;
                                        break;
                                    }
                                    else
                                    {
                                        if (fallbackFixedVersion == null)
                                        {
                                            fallbackFixedVersion = ExtractAnyFixedVersion(affected);
                                        }
                                    }
                                }
                            }
                        }

                        fixedVersion = foundCorrectBlock ? targetFixedVersion : fallbackFixedVersion;

                        vulnerabilities.Add(new OsvVulnerabilityDto(id, alias, summary, details, severityType, severityScore, severityLevel, fixedVersion, vuln.GetRawText()));
                    }
                }
            }
            catch (JsonException)
            {
            }
            return vulnerabilities;
        }

        private static bool PackageMatches(JsonElement affected, string targetPurl)
        {
            if (affected.TryGetProperty("package", out var packageProp))
            {
                if (packageProp.TryGetProperty("purl", out var affectedPurlProp))
                {
                    var affectedPurl = affectedPurlProp.GetString();
                    if (!string.IsNullOrEmpty(affectedPurl))
                    {
                        var cleanAffected = CleanPurlForComparison(affectedPurl);
                        var cleanTarget = CleanPurlForComparison(targetPurl);
                        if (string.Equals(cleanAffected, cleanTarget, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }

                if (packageProp.TryGetProperty("name", out var nameProp))
                {
                    var name = nameProp.GetString();
                    var targetName = ExtractPackageNameFromPurl(targetPurl);
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(targetName) &&
                        string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            else
            {
                return true;
            }

            return false;
        }

        private static string CleanPurlForComparison(string purl)
        {
            var atIndex = purl.LastIndexOf('@');
            if (atIndex > 0)
            {
                purl = purl.Substring(0, atIndex);
            }
            return Uri.UnescapeDataString(purl).Trim().ToLowerInvariant();
        }

        private static string ExtractPackageNameFromPurl(string purl)
        {
            var decoded = Uri.UnescapeDataString(purl);
            var schemeIndex = decoded.IndexOf(':');
            if (schemeIndex > 0)
            {
                var path = decoded.Substring(schemeIndex + 1);
                var slashIndex = path.IndexOf('/');
                if (slashIndex > 0)
                {
                    return path.Substring(slashIndex + 1);
                }
            }
            var lastSlash = decoded.LastIndexOf('/');
            if (lastSlash > 0)
            {
                return decoded.Substring(lastSlash + 1);
            }
            return purl;
        }

        private static string? ExtractAnyFixedVersion(JsonElement affected)
        {
            if (affected.TryGetProperty("ranges", out var rangesArray) && rangesArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var range in rangesArray.EnumerateArray())
                {
                    if (range.TryGetProperty("events", out var eventsArray) && eventsArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var evt in eventsArray.EnumerateArray())
                        {
                            if (evt.TryGetProperty("fixed", out var fixedProp))
                                return fixedProp.GetString();
                        }
                    }
                }
            }
            return null;
        }

        private static bool IsVersionInRange(string currentVersion, JsonElement rangeElement)
        {
            if (!rangeElement.TryGetProperty("events", out var eventsArray) || eventsArray.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            string? introducedVersion = null;
            string? fixedVersion = null;
            string? lastAffectedVersion = null;

            foreach (var evt in eventsArray.EnumerateArray())
            {
                if (evt.TryGetProperty("introduced", out var introProp))
                {
                    if (introducedVersion != null)
                    {
                        if (IsVersionInInterval(currentVersion, introducedVersion, fixedVersion, lastAffectedVersion))
                        {
                            return true;
                        }
                    }
                    introducedVersion = introProp.GetString();
                    fixedVersion = null;
                    lastAffectedVersion = null;
                }
                else if (evt.TryGetProperty("fixed", out var fixedProp))
                {
                    fixedVersion = fixedProp.GetString();
                    if (introducedVersion != null)
                    {
                        if (IsVersionInInterval(currentVersion, introducedVersion, fixedVersion, lastAffectedVersion))
                        {
                            return true;
                        }
                        introducedVersion = null;
                        fixedVersion = null;
                        lastAffectedVersion = null;
                    }
                }
                else if (evt.TryGetProperty("last_affected", out var lastAffectedProp))
                {
                    lastAffectedVersion = lastAffectedProp.GetString();
                    if (introducedVersion != null)
                    {
                        if (IsVersionInInterval(currentVersion, introducedVersion, fixedVersion, lastAffectedVersion))
                        {
                            return true;
                        }
                        introducedVersion = null;
                        fixedVersion = null;
                        lastAffectedVersion = null;
                    }
                }
            }

            if (introducedVersion != null)
            {
                if (IsVersionInInterval(currentVersion, introducedVersion, fixedVersion, lastAffectedVersion))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsVersionInInterval(string currentVersion, string introduced, string? fixedVer, string? lastAffectedVer)
        {
            if (introduced != "0" && CompareVersions(currentVersion, introduced) < 0)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(fixedVer))
            {
                if (CompareVersions(currentVersion, fixedVer) >= 0)
                {
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(lastAffectedVer))
            {
                if (CompareVersions(currentVersion, lastAffectedVer) > 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static int CompareVersions(string versionA, string versionB)
        {
            if (string.Equals(versionA, versionB, StringComparison.OrdinalIgnoreCase))
                return 0;

            var cleanA = CleanVersion(versionA);
            var cleanB = CleanVersion(versionB);

            if (string.Equals(cleanA, cleanB, StringComparison.OrdinalIgnoreCase))
                return 0;

            var mainPartA = cleanA.Split('-')[0];
            var mainPartB = cleanB.Split('-')[0];

            var partsA = mainPartA.Split('.');
            var partsB = mainPartB.Split('.');

            var maxLength = Math.Max(partsA.Length, partsB.Length);
            for (int i = 0; i < maxLength; i++)
            {
                var partAStr = i < partsA.Length ? partsA[i] : "0";
                var partBStr = i < partsB.Length ? partsB[i] : "0";

                var isANum = int.TryParse(partAStr, out var numA);
                var isBNum = int.TryParse(partBStr, out var numB);

                if (isANum && isBNum)
                {
                    if (numA != numB)
                        return numA.CompareTo(numB);
                  }
                  else
                  {
                      var comp = string.Compare(partAStr, partBStr, StringComparison.OrdinalIgnoreCase);
                      if (comp != 0)
                          return comp;
                  }
              }

              var hasPreA = cleanA.Contains('-');
              var hasPreB = cleanB.Contains('-');
              if (hasPreA && !hasPreB) return -1;
              if (!hasPreA && hasPreB) return 1;

              if (hasPreA && hasPreB)
              {
                  var preA = cleanA.Split('-', 2)[1];
                  var preB = cleanB.Split('-', 2)[1];
                  return string.Compare(preA, preB, StringComparison.OrdinalIgnoreCase);
              }

              return 0;
          }

          private static string CleanVersion(string version)
          {
              if (string.IsNullOrWhiteSpace(version)) return "0";
              version = version.Trim();
              if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
              {
                  version = version.Substring(1);
              }
              return version.TrimStart('.');
          }
      }
  }
