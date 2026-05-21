using AutoMapper;
using Sentinel.Application.Abstractions;
using Sentinel.Application.DTOs.Requests;
using Sentinel.Application.DTOs.Responses;
using Sentinel.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sentinel.Application.Services
{
    public class WorkspaceService : IWorkspaceService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public WorkspaceService(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<BaseResponse<List<WorkspaceResponse>>> GetAllByOwnerAsync(Guid ownerId)
        {
            var workspaces = await _unitOfWork.Workspaces
                .GetAllAsync(w => w.OwnerId == ownerId);
            
            var responseList = new List<WorkspaceResponse>();
            foreach (var workspace in workspaces)
            {
                var (threatScore, licenseScore) = await GetWorkspaceScoresAsync(workspace.Id);
                responseList.Add(new WorkspaceResponse(
                    workspace.Id, 
                    workspace.Name, 
                    workspace.Description, 
                    workspace.CreatedAt, 
                    threatScore, 
                    licenseScore
                ));
            }
            return BaseResponse<List<WorkspaceResponse>>.Ok(responseList);
        }

        public async Task<BaseResponse<WorkspaceResponse>> GetByIdAsync(Guid id, Guid ownerId)
        {
            var workspace = await _unitOfWork.Workspaces
                .GetByIdAsync(id);

            if (workspace == null || workspace.OwnerId != ownerId)
                return BaseResponse<WorkspaceResponse>.Fail("Workspace not found");

            var (threatScore, licenseScore) = await GetWorkspaceScoresAsync(workspace.Id);

            var response = new WorkspaceResponse(
                workspace.Id,
                workspace.Name,
                workspace.Description,
                workspace.CreatedAt,
                threatScore,
                licenseScore
            );

            return BaseResponse<WorkspaceResponse>.Ok(response);
        }

        private async Task<(double? ThreatScore, double? LicenseScore)> GetWorkspaceScoresAsync(Guid workspaceId)
        {
            var modules = await _unitOfWork.Modules.GetAllAsync(
                m => m.WorkspaceId == workspaceId, 
                includeProperties: "Scans.Components.VexStatements.Vulnerability"
            );

            if (modules == null || !modules.Any())
                return (null, null);

            // Collect all unique PURLs across all modules' latest scans
            var allLatestPurls = modules
                .Select(m => m.Scans?.OrderByDescending(s => s.ScanDate).FirstOrDefault())
                .Where(s => s != null)
                .SelectMany(s => s!.Components ?? new List<Component>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Purl))
                .Select(c => c.Purl!)
                .Distinct()
                .ToList();

            HashSet<string> riskPurls = new HashSet<string>();
            IEnumerable<PackageLicense> riskPackageLicenses = new List<PackageLicense>();

            if (allLatestPurls.Any())
            {
                riskPackageLicenses = await _unitOfWork.PackageLicenses.GetAllAsync(
                    pl => allLatestPurls.Contains(pl.Purl) && (pl.License.RiskLevel == "High" || pl.License.RiskLevel == "Medium"),
                    includeProperties: "License"
                );
                riskPurls = riskPackageLicenses.Select(pl => pl.Purl).ToHashSet();
            }

            var moduleScores = modules.Select(m => {
                var latestScan = m.Scans?.OrderByDescending(s => s.ScanDate).FirstOrDefault();
                return CalculateModuleScores(latestScan, riskPurls, riskPackageLicenses);
            }).ToList();

            double? threatScore = null;
            var validThreatScores = moduleScores.Where(s => s.ThreatScore.HasValue).Select(s => s.ThreatScore!.Value).ToList();
            if (validThreatScores.Any())
            {
                threatScore = validThreatScores.Max();
            }

            double? licenseScore = null;
            var validLicenseScores = moduleScores.Where(s => s.LicenseScore.HasValue).Select(s => s.LicenseScore!.Value).ToList();
            if (validLicenseScores.Any())
            {
                licenseScore = validLicenseScores.Min();
            }

            return (threatScore, licenseScore);
        }

        private static (double? ThreatScore, double? LicenseScore) CalculateModuleScores(
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

        public async Task<BaseResponse<Guid>> CreateAsync(WorkspaceRequest request, Guid ownerId)
        {
            var workspace = new Workspace
            {
                Name = request.Name,
                Description = request.Description,
                OwnerId = ownerId
            };

            await _unitOfWork.Workspaces.AddAsync(workspace);
            await _unitOfWork.SaveChangesAsync();

            return BaseResponse<Guid>.Ok(workspace.Id, "Workspace created successfully");
        }

        public async Task<BaseResponse<bool>> UpdateAsync(Guid id, WorkspaceRequest request, Guid ownerId)
        {
            var workspace = await _unitOfWork.Workspaces.GetByIdAsync(id);

            if (workspace == null || workspace.OwnerId != ownerId)
                return BaseResponse<bool>.Fail("Workspace not found");

            workspace.Name = request.Name;
            workspace.Description = request.Description;

            _unitOfWork.Workspaces.Update(workspace);
            await _unitOfWork.SaveChangesAsync();

            return BaseResponse<bool>.Ok(true, "Workspace updated successfully");
        }

        public async Task<BaseResponse<bool>> DeleteAsync(Guid id, Guid ownerId)
        {
            var workspace = await _unitOfWork.Workspaces.GetByIdAsync(id);

            if (workspace == null || workspace.OwnerId != ownerId)
                return BaseResponse<bool>.Fail("Workspace not found");

            _unitOfWork.Workspaces.Delete(workspace);
            await _unitOfWork.SaveChangesAsync();

            return BaseResponse<bool>.Ok(true, "Workspace deleted successfully");
        }
    }
}
