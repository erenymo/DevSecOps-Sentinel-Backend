using Sentinel.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Application.Abstractions
{
    public interface IUnitOfWork : IDisposable
    {
        IGenericRepository<Workspace> Workspaces { get; }
        IGenericRepository<Module> Modules { get; }
        IGenericRepository<Scan> Scans { get; }
        IGenericRepository<Component> Components { get; }
        IGenericRepository<Vulnerability> Vulnerabilities { get; }
        IGenericRepository<VexStatement> VexStatements { get; }
        IGenericRepository<License> Licenses { get; }
        IGenericRepository<ComponentLicense> ComponentLicenses { get; }

        Task<int> SaveChangesAsync();
    }
}
