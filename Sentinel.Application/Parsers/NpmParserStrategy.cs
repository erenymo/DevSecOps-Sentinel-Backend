using Sentinel.Application.Abstractions;
using Sentinel.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sentinel.Application.Parsers
{
    /// <summary>
    /// npm ekosistemi için parser stratejisi.
    /// package.json (sadece direct) ve package-lock.json (direct + transitive) dosyalarını destekler.
    /// İçerik tabanlı routing: "lockfileVersion" property'si varsa package-lock.json olarak işlenir.
    /// </summary>
    public class NpmParserStrategy : IParserStrategy
    {
        public string Ecosystem => "npm";

        // JSON derinlik limiti — aşırı derin iç içe yapılarla yapılabilecek DoS saldırılarına karşı koruma
        private const int MaxJsonDepth = 128;

        public async Task<List<Component>> ParseAsync(Stream stream, string extension, Guid scanId)
        {
            // 1. İçeriğe göre formatı belirle (ikisi de .json uzantılı)
            // Stream'i bir kez parse edip hem tür tespiti hem veri çıkarımı yapacağız.
            var options = new JsonDocumentOptions
            {
                MaxDepth = MaxJsonDepth,
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            };

            using var doc = await JsonDocument.ParseAsync(stream, options);
            var root = doc.RootElement;

            // "lockfileVersion" property'si varsa → package-lock.json
            if (root.TryGetProperty("lockfileVersion", out _))
            {
                return ParsePackageLockJson(root, scanId);
            }

            // Yoksa → package.json
            return ParsePackageJson(root, scanId);
        }

        /// <summary>
        /// package.json dosyasını parse eder.
        /// Bu dosya sadece doğrudan bağımlılıkları (direct dependencies) içerir.
        /// "dependencies" ve "devDependencies" okunur.
        /// </summary>
        private List<Component> ParsePackageJson(JsonElement root, Guid scanId)
        {
            var components = new List<Component>();

            // Proje adını "name" property'sinden oku
            string projectName = root.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? "UnknownProject"
                : "UnknownProject";

            // dependencies
            if (root.TryGetProperty("dependencies", out var deps))
            {
                AddDirectDependencies(deps, components, scanId, projectName);
            }

            // devDependencies
            if (root.TryGetProperty("devDependencies", out var devDeps))
            {
                AddDirectDependencies(devDeps, components, scanId, projectName);
            }

            return components;
        }

        /// <summary>
        /// Bir dependency bloğundaki tüm paketleri doğrudan bağımlılık olarak ekler.
        /// package.json'daki "dependencies" ve "devDependencies" için kullanılır.
        /// </summary>
        private void AddDirectDependencies(JsonElement depsNode, List<Component> components, Guid scanId, string projectName)
        {
            foreach (var dep in depsNode.EnumerateObject())
            {
                var name = dep.Name;
                var versionRange = dep.Value.GetString() ?? "0.0.0";

                // Semver range temizleme (^, ~, >=, vb. kaldır)
                var cleanVersion = CleanSemverRange(versionRange);

                // npm scoped paketler için purl: pkg:npm/%40scope/name@version
                var purl = BuildNpmPurl(name, cleanVersion);

                components.Add(new Component
                {
                    Id = Guid.NewGuid(),
                    ScanId = scanId,
                    Name = name,
                    Version = cleanVersion,
                    Purl = purl,
                    IsTransitive = false,
                    ParentName = null,
                    DependencyPath = $"{projectName} -> {name}",
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// package-lock.json dosyasını parse eder (lockfileVersion 2 ve 3 destekli).
        /// "packages" düğümünü kullanır. Root entry ("") içindeki dependencies
        /// doğrudan bağımlılıklardır; diğerleri transitive'dir.
        /// </summary>
        private List<Component> ParsePackageLockJson(JsonElement root, Guid scanId)
        {
            var components = new List<Component>();

            // Root proje adı
            string projectName = root.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? "UnknownProject"
                : "UnknownProject";

            // lockfileVersion 2/3 → "packages" düğümünü kullan
            if (root.TryGetProperty("packages", out var packages))
            {
                var directDependencyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 1. ADIM: Root entry'den doğrudan bağımlılık isimlerini çıkar
                if (packages.TryGetProperty("", out var rootEntry))
                {
                    CollectDirectDependencyNames(rootEntry, directDependencyNames);
                }

                var addedDirects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var directName in directDependencyNames)
                {
                    var directPath = $"node_modules/{directName}";
                    if (packages.TryGetProperty(directPath, out var pkgElement))
                    {
                        var version = pkgElement.TryGetProperty("version", out var v) ? v.GetString() ?? "0.0.0" : "0.0.0";
                        var purl = BuildNpmPurl(directName, version);
                        var dependencyPath = $"{projectName} -> {directName}";

                        if (addedDirects.Add(directName))
                        {
                            components.Add(new Component
                            {
                                Id = Guid.NewGuid(),
                                ScanId = scanId,
                                Name = directName,
                                Version = version,
                                Purl = purl,
                                IsTransitive = false,
                                ParentName = null,
                                DependencyPath = dependencyPath,
                                CreatedAt = DateTime.UtcNow
                            });
                        }

                        // Recursive resolve transitives
                        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { directPath };
                        var addedTransitivesForThisDirect = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        ResolveDependenciesRecursive(
                            packages,
                            directPath,
                            directName,
                            directName,
                            visited,
                            addedTransitivesForThisDirect,
                            components,
                            scanId,
                            projectName
                        );
                    }
                }
            }
            // lockfileVersion 1 fallback → eski "dependencies" düğümünü kullan
            else if (root.TryGetProperty("dependencies", out var depsNode))
            {
                var addedDirects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var dep in depsNode.EnumerateObject())
                {
                    var directName = dep.Name;
                    var version = dep.Value.TryGetProperty("version", out var v) ? v.GetString() ?? "0.0.0" : "0.0.0";
                    var purl = BuildNpmPurl(directName, version);
                    var dependencyPath = $"{projectName} -> {directName}";

                    if (addedDirects.Add(directName))
                    {
                        components.Add(new Component
                        {
                            Id = Guid.NewGuid(),
                            ScanId = scanId,
                            Name = directName,
                            Version = version,
                            Purl = purl,
                            IsTransitive = false,
                            ParentName = null,
                            DependencyPath = dependencyPath,
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    // Recursive resolve transitives for lockfile v1
                    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { directName };
                    var addedTransitivesForThisDirect = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    ResolveV1DependenciesRecursive(
                        depsNode,
                        directName,
                        directName,
                        directName,
                        visited,
                        addedTransitivesForThisDirect,
                        components,
                        scanId,
                        projectName
                    );
                }
            }

            return components;
        }

        private void ResolveDependenciesRecursive(
            JsonElement packages,
            string currentPkgPath,
            string directParentName,
            string currentPkgName,
            HashSet<string> visitedPaths,
            HashSet<string> addedTransitivesForThisDirect,
            List<Component> components,
            Guid scanId,
            string projectName)
        {
            if (!packages.TryGetProperty(currentPkgPath, out var pkgElement))
                return;

            if (pkgElement.TryGetProperty("dependencies", out var depsElement))
            {
                foreach (var dep in depsElement.EnumerateObject())
                {
                    var depName = dep.Name;

                    var resolvedPath = ResolvePackagePath(packages, currentPkgPath, depName);
                    if (string.IsNullOrEmpty(resolvedPath))
                    {
                        resolvedPath = $"node_modules/{depName}";
                        if (!packages.TryGetProperty(resolvedPath, out _))
                        {
                            continue;
                        }
                    }

                    if (visitedPaths.Contains(resolvedPath))
                        continue;

                    if (packages.TryGetProperty(resolvedPath, out var depPkgElement))
                    {
                        var version = depPkgElement.TryGetProperty("version", out var v) ? v.GetString() ?? "0.0.0" : "0.0.0";
                        var purl = BuildNpmPurl(depName, version);
                        var dependencyPath = $"{projectName} -> {directParentName} -> ... -> {depName}";

                        if (addedTransitivesForThisDirect.Add(depName))
                        {
                            components.Add(new Component
                            {
                                Id = Guid.NewGuid(),
                                ScanId = scanId,
                                Name = depName,
                                Version = version,
                                Purl = purl,
                                IsTransitive = true,
                                ParentName = directParentName,
                                DependencyPath = dependencyPath,
                                CreatedAt = DateTime.UtcNow
                            });
                        }

                        var nextVisited = new HashSet<string>(visitedPaths, StringComparer.OrdinalIgnoreCase) { resolvedPath };
                        ResolveDependenciesRecursive(
                            packages,
                            resolvedPath,
                            directParentName,
                            depName,
                            nextVisited,
                            addedTransitivesForThisDirect,
                            components,
                            scanId,
                            projectName
                        );
                    }
                }
            }
        }

        private string? ResolvePackagePath(JsonElement packages, string currentPkgPath, string depName)
        {
            if (string.IsNullOrEmpty(currentPkgPath))
            {
                var rootPath = $"node_modules/{depName}";
                if (packages.TryGetProperty(rootPath, out _))
                    return rootPath;
                return null;
            }

            var parts = currentPkgPath.Split(new[] { "node_modules/" }, StringSplitOptions.RemoveEmptyEntries)
                                      .Select(p => p.TrimEnd('/')).ToList();

            for (int i = parts.Count; i >= 0; i--)
            {
                var subParts = parts.Take(i);
                var pathBuilder = new StringBuilder();
                foreach (var part in subParts)
                {
                    pathBuilder.Append("node_modules/");
                    pathBuilder.Append(part);
                    pathBuilder.Append("/");
                }
                pathBuilder.Append("node_modules/");
                pathBuilder.Append(depName);

                var candidatePath = pathBuilder.ToString();
                if (packages.TryGetProperty(candidatePath, out _))
                {
                    return candidatePath;
                }
            }

            return null;
        }

        private void ResolveV1DependenciesRecursive(
            JsonElement rootDeps,
            string currentPkgPath,
            string directParentName,
            string currentPkgName,
            HashSet<string> visitedPaths,
            HashSet<string> addedTransitivesForThisDirect,
            List<Component> components,
            Guid scanId,
            string projectName)
        {
            var pkgElement = GetV1PackageAtPath(rootDeps, currentPkgPath);
            if (pkgElement == null || !pkgElement.Value.TryGetProperty("requires", out var requiresElement))
                return;

            foreach (var req in requiresElement.EnumerateObject())
            {
                var depName = req.Name;

                var resolvedPath = ResolveV1PackagePath(rootDeps, currentPkgPath, depName);
                if (string.IsNullOrEmpty(resolvedPath))
                {
                    resolvedPath = depName;
                }

                if (visitedPaths.Contains(resolvedPath))
                    continue;

                var depPkgElement = GetV1PackageAtPath(rootDeps, resolvedPath);
                if (depPkgElement != null)
                {
                    var version = depPkgElement.Value.TryGetProperty("version", out var v) ? v.GetString() ?? "0.0.0" : "0.0.0";
                    var purl = BuildNpmPurl(depName, version);
                    var dependencyPath = $"{projectName} -> {directParentName} -> ... -> {depName}";

                    if (addedTransitivesForThisDirect.Add(depName))
                    {
                        components.Add(new Component
                        {
                            Id = Guid.NewGuid(),
                            ScanId = scanId,
                            Name = depName,
                            Version = version,
                            Purl = purl,
                            IsTransitive = true,
                            ParentName = directParentName,
                            DependencyPath = dependencyPath,
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    var nextVisited = new HashSet<string>(visitedPaths, StringComparer.OrdinalIgnoreCase) { resolvedPath };
                    ResolveV1DependenciesRecursive(
                        rootDeps,
                        resolvedPath,
                        directParentName,
                        depName,
                        nextVisited,
                        addedTransitivesForThisDirect,
                        components,
                        scanId,
                        projectName
                    );
                }
            }
        }

        private string? ResolveV1PackagePath(JsonElement rootDeps, string currentPkgPath, string depName)
        {
            if (string.IsNullOrEmpty(currentPkgPath))
            {
                if (rootDeps.TryGetProperty(depName, out _))
                    return depName;
                return null;
            }

            var parts = currentPkgPath.Split('/').ToList();

            for (int i = parts.Count; i >= 0; i--)
            {
                var prefixParts = parts.Take(i).ToList();
                prefixParts.Add(depName);
                var candidatePath = string.Join("/", prefixParts);

                if (GetV1PackageAtPath(rootDeps, candidatePath) != null)
                {
                    return candidatePath;
                }
            }

            return null;
        }

        private JsonElement? GetV1PackageAtPath(JsonElement rootDeps, string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var parts = path.Split('/');
            var currentElement = rootDeps;

            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    if (currentElement.TryGetProperty("dependencies", out var subDeps))
                    {
                        currentElement = subDeps;
                    }
                    else
                    {
                        return null;
                    }
                }

                if (currentElement.TryGetProperty(parts[i], out var next))
                {
                    currentElement = next;
                }
                else
                {
                    return null;
                }
            }

            return currentElement;
        }

        /// <summary>
        /// Root entry içindeki "dependencies" ve "devDependencies" isimlerini toplar.
        /// Bu isimler doğrudan bağımlılıklar olarak işaretlenecektir.
        /// </summary>
        private void CollectDirectDependencyNames(JsonElement rootEntry, HashSet<string> directNames)
        {
            if (rootEntry.TryGetProperty("dependencies", out var deps))
            {
                foreach (var dep in deps.EnumerateObject())
                    directNames.Add(dep.Name);
            }
            if (rootEntry.TryGetProperty("devDependencies", out var devDeps))
            {
                foreach (var dep in devDeps.EnumerateObject())
                    directNames.Add(dep.Name);
            }
        }

        /// <summary>
        /// DependencyPath oluşturur.
        /// parentMap üzerinden üst ebeveynleri takip eder.
        /// </summary>
        private string BuildDependencyPath(string packageName, Dictionary<string, string> parentMap, string projectName)
        {
            var pathList = new List<string> { packageName };
            var current = parentMap.TryGetValue(packageName, out var p) ? p : null;

            // Sonsuz döngü koruması (maksimum 50 seviye)
            int safetyCounter = 0;
            const int maxDepth = 50;

            while (!string.IsNullOrEmpty(current) && safetyCounter < maxDepth)
            {
                pathList.Add(current);
                current = parentMap.TryGetValue(current, out var nextP) ? nextP : null;
                safetyCounter++;
            }

            if (!pathList.Contains(projectName))
                pathList.Add(projectName);

            pathList.Reverse();
            return string.Join(" -> ", pathList);
        }

        /// <summary>
        /// npm Package URL (purl) oluşturur.
        /// Scoped paketler için (@scope/name) URL encoding uygular.
        /// Spesifikasyon: https://github.com/package-url/purl-spec
        /// </summary>
        private string BuildNpmPurl(string name, string version)
        {
            if (name.StartsWith("@"))
            {
                // Scoped paketler: pkg:npm/%40scope/name@version
                // '@' karakteri '%40' olarak encode edilir (purl spec)
                var encodedName = name.Replace("@", "%40");
                return $"pkg:npm/{encodedName}@{version}";
            }

            return $"pkg:npm/{name}@{version}";
        }

        /// <summary>
        /// Semver range prefix'lerini temizler.
        /// Örn: "^1.2.3" → "1.2.3", "~2.0.0" → "2.0.0", ">=1.0.0" → "1.0.0"
        /// </summary>
        private string CleanSemverRange(string versionRange)
        {
            if (string.IsNullOrWhiteSpace(versionRange))
                return "0.0.0";

            // Range operatörlerini temizle
            var cleaned = versionRange
                .TrimStart('^', '~', '>', '<', '=', ' ');

            // "x" range desteği: "1.x" → "1.0.0"
            cleaned = cleaned.Replace(".x", ".0").Replace(".*", ".0");

            // Eğer hiçbir şey kalmadıysa
            if (string.IsNullOrWhiteSpace(cleaned))
                return "0.0.0";

            return cleaned;
        }
    }
}
