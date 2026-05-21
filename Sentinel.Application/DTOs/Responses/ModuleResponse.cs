namespace Sentinel.Application.DTOs.Responses
{
    public record ModuleResponse(Guid Id, string Name, string Ecosystem, string RootPath, Guid WorkspaceId, DateTime CreatedAt, int DependencyCount = 0, int VulnerabilityCount = 0, DateTime? LastScanDate = null, int LicenseIssueCount = 0, double? ThreatScore = null, double? LicenseScore = null);
}

