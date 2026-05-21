using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CycloneDX;
using CycloneDX.Models;
using CycloneDX.Models.Vulnerabilities;
using CycloneDX.Json;
using Sentinel.Application.Abstractions;
using Sentinel.Application.DTOs.Responses;
using Sentinel.Domain.Entities;

namespace Sentinel.Application.Services
{
    public class SbomExportService : ISbomExportService
    {
        private readonly IUnitOfWork _unitOfWork;

        public SbomExportService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResponse<string>> ExportSbomJsonAsync(Guid moduleId, Guid ownerId)
        {
            // 1. Get module and verify ownership
            var module = (await _unitOfWork.Modules.GetAllAsync(
                m => m.Id == moduleId,
                includeProperties: "Workspace"
            )).FirstOrDefault();

            if (module == null)
            {
                return BaseResponse<string>.Fail("Module not found");
            }

            if (module.Workspace == null || module.Workspace.OwnerId != ownerId)
            {
                return BaseResponse<string>.Fail("Module not found");
            }

            // 2. Get latest scan with components, vex statements, and vulnerabilities
            var scans = await _unitOfWork.Scans.GetAllAsync(
                s => s.ModuleId == moduleId,
                includeProperties: "Components.VexStatements.Vulnerability"
            );
            var latestScan = scans.OrderByDescending(s => s.ScanDate).FirstOrDefault();

            if (latestScan == null)
            {
                return BaseResponse<string>.Fail("No scan data found for this module. Please scan it first.");
            }

            // Retrieve licenses for the components in the latest scan
            var purls = latestScan.Components
                .Select(c => c.Purl)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToList();

            var packageLicenses = new List<PackageLicense>();
            if (purls.Any())
            {
                packageLicenses = (await _unitOfWork.PackageLicenses.GetAllAsync(
                    pl => purls.Contains(pl.Purl),
                    includeProperties: "License"
                )).ToList();
            }

            var licenseMap = packageLicenses
                .Where(pl => pl.License != null)
                .GroupBy(pl => pl.Purl)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(pl => pl.License.Name).Distinct().ToList(),
                    StringComparer.OrdinalIgnoreCase
                );

            // 3. Initialize CycloneDX BOM
            var bom = new Bom
            {
                SpecVersion = SpecificationVersion.v1_5,
                Components = new List<CycloneDX.Models.Component>(),
                Vulnerabilities = new List<CycloneDX.Models.Vulnerabilities.Vulnerability>()
            };

            var componentMap = new Dictionary<string, CycloneDX.Models.Component>();
            var vulnerabilityMap = new Dictionary<string, CycloneDX.Models.Vulnerabilities.Vulnerability>();

            // 4. Map Components
            foreach (var comp in latestScan.Components)
            {
                var bomRef = comp.Purl ?? comp.Name;
                
                var cdComponent = new CycloneDX.Models.Component
                {
                    BomRef = bomRef,
                    Name = comp.Name,
                    Version = comp.Version,
                    Purl = comp.Purl,
                    Type = CycloneDX.Models.Component.Classification.Library
                };

                if (!string.IsNullOrEmpty(comp.Purl) && licenseMap.TryGetValue(comp.Purl, out var licenseNames) && licenseNames.Any())
                {
                    cdComponent.Licenses = licenseNames.Select(name => new CycloneDX.Models.LicenseChoice
                    {
                        License = new CycloneDX.Models.License
                        {
                            Name = name
                        }
                    }).ToList();
                }

                bom.Components.Add(cdComponent);
                if (!componentMap.ContainsKey(bomRef))
                {
                    componentMap[bomRef] = cdComponent;
                }

                // 5. Map Vulnerabilities & VEX Statements
                if (comp.VexStatements != null)
                {
                    foreach (var vex in comp.VexStatements)
                    {
                        if (vex.Vulnerability == null) continue;

                        var vuln = vex.Vulnerability;
                        // Match on VulnerabilityId per user instructions:
                        // "Zafiyetlere ait Vex statementleri VexStatements Tablosundaki Status'tan alıyoruz. Eşleştirmeyi VulnerabilityId üzerinden yapıyoruz."
                        // We will use VulnerabilityId (or ExternalId as fallback) to identify it in SBOM.
                        var vulnKey = vuln.ExternalId ?? vuln.VulnerabilityId ?? vuln.Id.ToString();

                        if (!vulnerabilityMap.TryGetValue(vulnKey, out var cdVuln))
                        {
                            cdVuln = new CycloneDX.Models.Vulnerabilities.Vulnerability
                            {
                                Id = vulnKey,
                                Description = vuln.Description,
                                Ratings = new List<CycloneDX.Models.Vulnerabilities.Rating>(),
                                Affects = new List<CycloneDX.Models.Vulnerabilities.Affects>()
                            };

                            // Map severity and score
                            if (vuln.SeverityScore.HasValue)
                            {
                                var rating = new CycloneDX.Models.Vulnerabilities.Rating
                                {
                                    Score = (double)vuln.SeverityScore.Value,
                                    Method = ScoreMethod.CVSSV3
                                };

                                if (!string.IsNullOrEmpty(vuln.SeverityLevel))
                                {
                                    var level = vuln.SeverityLevel.ToLowerInvariant();
                                    rating.Severity = level switch
                                    {
                                        "critical" => CycloneDX.Models.Vulnerabilities.Severity.Critical,
                                        "high" => CycloneDX.Models.Vulnerabilities.Severity.High,
                                        "medium" => CycloneDX.Models.Vulnerabilities.Severity.Medium,
                                        "low" => CycloneDX.Models.Vulnerabilities.Severity.Low,
                                        "info" => CycloneDX.Models.Vulnerabilities.Severity.Info,
                                        "none" => CycloneDX.Models.Vulnerabilities.Severity.None,
                                        _ => CycloneDX.Models.Vulnerabilities.Severity.Unknown
                                    };
                                }
                                else
                                {
                                    rating.Severity = CycloneDX.Models.Vulnerabilities.Severity.Unknown;
                                }

                                cdVuln.Ratings.Add(rating);
                            }

                            // Initialize VEX analysis
                            cdVuln.Analysis = new CycloneDX.Models.Vulnerabilities.Analysis();

                            vulnerabilityMap[vulnKey] = cdVuln;
                            bom.Vulnerabilities.Add(cdVuln);
                        }

                        // Map VEX Statement (Status -> Analysis State)
                        if (!string.IsNullOrEmpty(vex.Status))
                        {
                            var status = vex.Status.ToLowerInvariant().Replace(" ", "_");
                            cdVuln.Analysis.State = status switch
                            {
                                "resolved" => ImpactAnalysisState.Resolved,
                                "resolved_with_pedigree" => ImpactAnalysisState.Resolved_With_Pedigree,
                                "exploitable" => ImpactAnalysisState.Exploitable,
                                "affected" => ImpactAnalysisState.Exploitable,
                                "in_triage" => ImpactAnalysisState.In_Triage,
                                "under_investigation" => ImpactAnalysisState.In_Triage,
                                "false_positive" => ImpactAnalysisState.False_Positive,
                                "not_affected" => ImpactAnalysisState.Not_Affected,
                                _ => ImpactAnalysisState.In_Triage
                            };
                        }
                        else
                        {
                            cdVuln.Analysis.State = ImpactAnalysisState.In_Triage;
                        }

                        if (!string.IsNullOrWhiteSpace(vex.Analysis))
                        {
                            cdVuln.Analysis.Detail = vex.Analysis;
                        }

                        // Add Affected Component reference
                        if (!cdVuln.Affects.Any(a => a.Ref == bomRef))
                        {
                            cdVuln.Affects.Add(new CycloneDX.Models.Vulnerabilities.Affects
                            {
                                Ref = bomRef
                            });
                        }
                    }
                }
            }

            // 6. Serialize to JSON
            try
            {
                var jsonOutput = CycloneDX.Json.Serializer.Serialize(bom);
                return BaseResponse<string>.Ok(jsonOutput, "SBOM exported successfully.");
            }
            catch (Exception ex)
            {
                return BaseResponse<string>.Fail($"Failed to serialize SBOM: {ex.Message}");
            }
        }
    }
}
