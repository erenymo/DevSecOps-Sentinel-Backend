using Sentinel.Application.Abstractions;
using Sentinel.Domain.Entities;
using Sentinel.Infrastructure.Persistence.Context;
using Sentinel.Infrastructure.Persistence.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Infrastructure.Persistence.UnitOfWork
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly SentinelDbContext _context;
        public UnitOfWork(SentinelDbContext context)
        {
            _context = context;
            Workspaces = new GenericRepository<Workspace>(_context);
            Modules = new GenericRepository<Module>(_context);
            Scans = new GenericRepository<Scan>(_context);
            Components = new GenericRepository<Component>(_context);
            Vulnerabilities = new GenericRepository<Vulnerability>(_context);
            VexStatements = new GenericRepository<VexStatement>(_context);
            Licenses = new GenericRepository<License>(_context);
            ComponentLicenses = new GenericRepository<ComponentLicense>(_context);
        }

        public IGenericRepository<Workspace> Workspaces { get; private set; }
        public IGenericRepository<Module> Modules { get; private set; }
        public IGenericRepository<Scan> Scans { get; private set; }
        public IGenericRepository<Component> Components { get; private set; }
        public IGenericRepository<Vulnerability> Vulnerabilities { get; private set; }
        public IGenericRepository<VexStatement> VexStatements { get; private set; }
        public IGenericRepository<License> Licenses { get; private set; }
        public IGenericRepository<ComponentLicense> ComponentLicenses { get; private set; }

        public async Task<int> SaveChangesAsync() => await _context.SaveChangesAsync();

        public void Dispose() => _context.Dispose();
    }
}
