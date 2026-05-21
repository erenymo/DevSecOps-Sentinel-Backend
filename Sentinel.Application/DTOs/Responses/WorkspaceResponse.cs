namespace Sentinel.Application.DTOs.Responses
{
    public record WorkspaceResponse(
        Guid Id, 
        string Name, 
        string? Description, 
        DateTime CreatedAt, 
        double? ThreatScore = null, 
        double? LicenseScore = null
    );
}
