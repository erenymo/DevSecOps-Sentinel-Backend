using AutoMapper;
using Sentinel.Application.Abstractions;
using Sentinel.Application.DTOs.Requests;
using Sentinel.Application.DTOs.Responses;
using Sentinel.Domain.Entities;

namespace Sentinel.Application.Services
{
    public class ModuleService : IModuleService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public ModuleService(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        private static (double? ThreatScore, double? LicenseScore) CalculateScores(
            Scan? latestScan, 
            HashSet<string> riskPurls, 
            IEnumerable<PackageLicense> riskPackageLicenses)
        {
            double? threatScore = null;
            if (latestScan?.Components != null && latestScan.Components.Any())
            {
                var allVulnerabilities = latestScan.Components
                    .SelectMany(c => c.VexStatements ?? new List<VexStatement>())
                    .Where(v => v.Vulnerability != null)
                    .Select(v => v.Vulnerability)
                    .ToList();

                if (allVulnerabilities.Any())
                {
                    var maxScore = allVulnerabilities.Max(v => (double)(v.SeverityScore ?? 0));
                    threatScore = Math.Round(maxScore, 1);
                }
            }

            double? licenseScore = null;
            if (latestScan?.Components != null && latestScan.Components.Any())
            {
                double score = 100.0;
                var componentsWithLicenses = latestScan.Components
                    .Where(c => !string.IsNullOrWhiteSpace(c.Purl))
                    .ToList();

                if (componentsWithLicenses.Any())
                {
                    foreach (var comp in componentsWithLicenses)
                    {
                        var matches = riskPackageLicenses.Where(pl => pl.Purl == comp.Purl).ToList();
                        if (matches.Any())
                        {
                            bool isHighRisk = matches.Any(pl => pl.License.RiskLevel == "High");
                            bool isMediumRisk = matches.Any(pl => pl.License.RiskLevel == "Medium");
                            double penalty = isHighRisk ? 25.0 : (isMediumRisk ? 10.0 : 0.0);
                            double weight = comp.IsTransitive ? 0.5 : 1.0;
                            score -= (penalty * weight);
                        }
                    }
                    licenseScore = Math.Max(0.0, Math.Min(100.0, Math.Round(score)));
                }
            }

            return (threatScore, licenseScore);
        }

        public async Task<BaseResponse<List<ModuleResponse>>> GetByWorkspaceAsync(Guid workspaceId, Guid ownerId)
        {
            // Verify workspace ownership
            var workspace = await _unitOfWork.Workspaces.GetByIdAsync(workspaceId);
            if (workspace == null || workspace.OwnerId != ownerId)
                return BaseResponse<List<ModuleResponse>>.Fail("Workspace not found");

            var modules = await _unitOfWork.Modules.GetAllAsync(m => m.WorkspaceId == workspaceId, includeProperties: "Scans.Components.VexStatements.Vulnerability");
            
            // Collect all unique PURLs across all modules' latest scans to query licenses efficiently in one round-trip
            var allLatestPurls = modules
                .Select(m => m.Scans?.OrderByDescending(s => s.ScanDate).FirstOrDefault())
                .Where(s => s != null)
                .SelectMany(s => s!.Components ?? new List<Component>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Purl))
                .Select(c => c.Purl!)
                .Distinct()
                .ToList();

            var riskPackageLicenses = await _unitOfWork.PackageLicenses.GetAllAsync(
                pl => allLatestPurls.Contains(pl.Purl) && (pl.License.RiskLevel == "High" || pl.License.RiskLevel == "Medium"),
                includeProperties: "License"
            );

            var riskPurls = riskPackageLicenses.Select(pl => pl.Purl).ToHashSet();

            var response = modules.Select(m => {
                var latestScan = m.Scans?.OrderByDescending(s => s.ScanDate).FirstOrDefault();
                int depCount = latestScan?.Components?.Count ?? 0;
                int vulnCount = latestScan?.Components?.SelectMany(c => c.VexStatements ?? new List<VexStatement>()).Count() ?? 0;
                
                int licenseIssueCount = latestScan?.Components?
                    .Where(c => !string.IsNullOrWhiteSpace(c.Purl) && riskPurls.Contains(c.Purl))
                    .Select(c => c.Purl!)
                    .Distinct()
                    .Count() ?? 0;

                var (threatScore, licenseScore) = CalculateScores(latestScan, riskPurls, riskPackageLicenses);

                return new ModuleResponse(m.Id, m.Name, m.Ecosystem, m.RootPath, m.WorkspaceId, m.CreatedAt, depCount, vulnCount, latestScan?.ScanDate, licenseIssueCount, threatScore, licenseScore);
            }).ToList();

            return BaseResponse<List<ModuleResponse>>.Ok(response);
        }

        public async Task<BaseResponse<ModuleResponse>> GetByIdAsync(Guid id, Guid ownerId)
        {
            var module = (await _unitOfWork.Modules.GetAllAsync(m => m.Id == id, includeProperties: "Scans.Components.VexStatements.Vulnerability")).FirstOrDefault();
            if (module == null)
                return BaseResponse<ModuleResponse>.Fail("Module not found");

            // Verify workspace ownership
            var workspace = await _unitOfWork.Workspaces.GetByIdAsync(module.WorkspaceId);
            if (workspace == null || workspace.OwnerId != ownerId)
                return BaseResponse<ModuleResponse>.Fail("Module not found");

            var latestScan = module.Scans?.OrderByDescending(s => s.ScanDate).FirstOrDefault();
            int depCount = latestScan?.Components?.Count ?? 0;
            int vulnCount = latestScan?.Components?.SelectMany(c => c.VexStatements ?? new List<VexStatement>()).Count() ?? 0;

            int licenseIssueCount = 0;
            double? threatScore = null;
            double? licenseScore = null;

            if (latestScan?.Components != null)
            {
                var purls = latestScan.Components
                    .Where(c => !string.IsNullOrWhiteSpace(c.Purl))
                    .Select(c => c.Purl!)
                    .Distinct()
                    .ToList();

                var riskPackageLicenses = await _unitOfWork.PackageLicenses.GetAllAsync(
                    pl => purls.Contains(pl.Purl) && (pl.License.RiskLevel == "High" || pl.License.RiskLevel == "Medium"),
                    includeProperties: "License"
                );

                var riskPurls = riskPackageLicenses.Select(pl => pl.Purl).ToHashSet();

                licenseIssueCount = latestScan.Components
                    .Where(c => !string.IsNullOrWhiteSpace(c.Purl) && riskPurls.Contains(c.Purl))
                    .Select(c => c.Purl!)
                    .Distinct()
                    .Count();

                var scores = CalculateScores(latestScan, riskPurls, riskPackageLicenses);
                threatScore = scores.ThreatScore;
                licenseScore = scores.LicenseScore;
            }

            return BaseResponse<ModuleResponse>.Ok(new ModuleResponse(module.Id, module.Name, module.Ecosystem, module.RootPath, module.WorkspaceId, module.CreatedAt, depCount, vulnCount, latestScan?.ScanDate, licenseIssueCount, threatScore, licenseScore));
        }

        public async Task<BaseResponse<Guid>> CreateAsync(Guid workspaceId, ModuleRequest request, Guid ownerId)
        {
            // Verify workspace ownership
            var workspace = await _unitOfWork.Workspaces.GetByIdAsync(workspaceId);
            if (workspace == null || workspace.OwnerId != ownerId)
                return BaseResponse<Guid>.Fail("Workspace not found");

            var module = new Module
            {
                Name = request.Name,
                Ecosystem = request.Ecosystem,
                RootPath = request.RootPath,
                WorkspaceId = workspaceId
            };

            await _unitOfWork.Modules.AddAsync(module);
            await _unitOfWork.SaveChangesAsync();

            return BaseResponse<Guid>.Ok(module.Id, "Module created successfully");
        }

        public async Task<BaseResponse<bool>> DeleteAsync(Guid id, Guid ownerId)
        {
            var module = await _unitOfWork.Modules.GetByIdAsync(id);
            if (module == null)
                return BaseResponse<bool>.Fail("Module not found");

            var workspace = await _unitOfWork.Workspaces.GetByIdAsync(module.WorkspaceId);
            if (workspace == null || workspace.OwnerId != ownerId)
                return BaseResponse<bool>.Fail("Module not found");

            _unitOfWork.Modules.Delete(module);
            await _unitOfWork.SaveChangesAsync();

            return BaseResponse<bool>.Ok(true, "Module deleted successfully");
        }
    }
}
