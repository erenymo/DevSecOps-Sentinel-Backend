using Microsoft.EntityFrameworkCore;
using Sentinel.Application.Abstractions;
using Sentinel.Infrastructure.Persistence.Context;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Infrastructure.Persistence.Repositories
{
    // Path: Sentinel.Infrastructure/Persistence/Repositories/GenericRepository.cs
    public class GenericRepository<T> : IGenericRepository<T> where T : class
    {
        protected readonly SentinelDbContext _context;
        private readonly DbSet<T> _dbSet;

        public GenericRepository(SentinelDbContext context)
        {
            _context = context;
            _dbSet = context.Set<T>();
        }

        public async Task<T?> GetByIdAsync(Guid id) => await _dbSet.FindAsync(id);

        public async Task<IEnumerable<T>> GetAllAsync() => await _dbSet.ToListAsync();

        public async Task<IEnumerable<T>> GetAllAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null) 
        {
            IQueryable<T> query = _dbSet;

            // Filtreyi uygula
            query = query.Where(predicate);

            // Ýliþkili tablolarý (Include) ekle
            if (!string.IsNullOrEmpty(includeProperties))
            {
                foreach (var includeProperty in includeProperties.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    query = query.Include(includeProperty);
                }
            }

            return await query.ToListAsync();
        }

        public IQueryable<T> Where(Expression<Func<T, bool>> predicate) => _dbSet.Where(predicate);

        public async Task AddAsync(T entity) => await _dbSet.AddAsync(entity);

        public void Update(T entity) => _dbSet.Update(entity);

        public void Delete(T entity) => _dbSet.Remove(entity);
    }
}
