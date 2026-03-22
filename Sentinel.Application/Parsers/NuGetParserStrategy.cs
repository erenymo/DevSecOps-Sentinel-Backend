using Sentinel.Application.Abstractions;
using Sentinel.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Sentinel.Application.Parsers
{
    public class NuGetParserStrategy : IParserStrategy
    {
        // NUGET project.assets.json dosyası yeterlidir çünkü bu dosya tüm bağımlılık ağacını içerir.
        // Bunun yanında .csproj dosyası da okunabilir ancak bu dosya sadece doğrudan bağımlılıkları içerir, transitive bağımlılıkları içermez.
        public string Ecosystem => "NuGet";

        public async Task<List<Component>> ParseAsync(string fileContent, Guid scanId)
        {
            fileContent = fileContent.Trim();

            // 1. Dosya tipini belirle
            if (fileContent.StartsWith("{"))
            {
                return await ParseProjectAssetsJsonAsync(fileContent, scanId);
            }
            else if (fileContent.StartsWith("<"))
            {
                return await ParseCsProjAsync(fileContent, scanId);
            }

            throw new ArgumentException("Desteklenmeyen dosya formatı. Lütfen .csproj veya project.assets.json yükleyin.");
        }

        private async Task<List<Component>> ParseCsProjAsync(string fileContent, Guid scanId)
        {
            var components = new List<Component>();
            var doc = XDocument.Parse(fileContent);

            // Proje adını Root elementinden veya dosya adından alabiliriz. 
            // Genelde .csproj içinde isim yazmaz, bu yüzden 'Project' diyelim.
            string projectName = "Project";

            // <PackageReference Include="Newtonsoft.Json" Version="13.0.1" /> formatını yakala
            var packageReferences = doc.Descendants("PackageReference");

            foreach (var pkg in packageReferences)
            {
                var name = pkg.Attribute("Include")?.Value;
                var version = pkg.Attribute("Version")?.Value ?? pkg.Element("Version")?.Value ?? "0.0.0";

                if (!string.IsNullOrEmpty(name))
                {
                    components.Add(new Component
                    {
                        Id = Guid.NewGuid(),
                        ScanId = scanId,
                        Name = name,
                        Version = version,
                        Purl = $"pkg:nuget/{name}@{version}",
                        IsTransitive = false, // .csproj içindekiler her zaman Direct'tir.
                        ParentName = null,
                        DependencyPath = $"{projectName} -> {name}",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            return components;
        }

        private async Task<List<Component>> ParseProjectAssetsJsonAsync(string fileContent, Guid scanId)
        {
            if (!fileContent.Contains("\"libraries\"") || !fileContent.Contains("\"targets\""))
            {
                throw new ArgumentException("Yüklenen dosya geçerli bir project.assets.json değil.");
            }

            var components = new List<Component>();
            var directDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var internalProjectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Hangi paketi kimin getirdiğini tutan harita (Key: Alt Paket, Value: Üst Paket)
            var parentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using var doc = JsonDocument.Parse(fileContent);
            var root = doc.RootElement;

            string rootProjectName = "Root";
            if (root.TryGetProperty("project", out var projectElement) &&
                projectElement.TryGetProperty("restore", out var restoreElement) &&
                restoreElement.TryGetProperty("projectName", out var nameElement))
            {
                rootProjectName = nameElement.GetString() ?? "Root";
            }

            // 1. ADIM: İç Projeleri Tespit Et
            if (root.TryGetProperty("libraries", out var libraries))
            {
                foreach (var lib in libraries.EnumerateObject())
                {
                    if (lib.Value.GetProperty("type").GetString() == "project")
                        internalProjectNames.Add(lib.Name.Split('/')[0]);
                }
            }

            // 2. ADIM: Dinamik Framework Taraması ve Parent/Direct Ayrımı
            if (root.TryGetProperty("targets", out var targets))
            {
                foreach (var framework in targets.EnumerateObject())
                {
                    foreach (var item in framework.Value.EnumerateObject())
                    {
                        var itemName = item.Name.Split('/')[0];
                        bool isInternal = internalProjectNames.Contains(itemName);

                        if (item.Value.TryGetProperty("dependencies", out var deps))
                        {
                            foreach (var dep in deps.EnumerateObject())
                            {
                                // Eğer bu bir iç projeyse, altındakiler Direct'tir
                                if (isInternal && !internalProjectNames.Contains(dep.Name))
                                {
                                    directDependencies.Add(dep.Name);
                                    // Direct paketlerin parent'ı projenin kendisidir
                                    if (!parentMap.ContainsKey(dep.Name)) parentMap[dep.Name] = itemName;
                                }
                                // Eğer bu bir paketse ve başka paketleri çağırıyorsa (Transitive)
                                else if (!isInternal)
                                {
                                    if (!parentMap.ContainsKey(dep.Name)) parentMap[dep.Name] = itemName;
                                }
                            }
                        }
                    }
                }
            }

            // 3. ADIM: Bileşenleri Oluşturma ve Tam Yol Analizi
            if (root.TryGetProperty("libraries", out var libs))
            {
                foreach (var library in libs.EnumerateObject())
                {
                    if (library.Value.GetProperty("type").GetString() == "package")
                    {
                        var parts = library.Name.Split('/');
                        var name = parts[0];
                        var version = parts[1];
                        bool isDirect = directDependencies.Contains(name);

                        var pathList = new List<string> { name };
                        var currentParent = parentMap.TryGetValue(name, out var p) ? p : null;

                        // Üst ebeveyn bitene kadar (Root'a ulaşana kadar) döngüye gir
                        while (!string.IsNullOrEmpty(currentParent))
                        {
                            pathList.Add(currentParent);
                            // Bir üst ebeveyne geç
                            currentParent = parentMap.TryGetValue(currentParent, out var nextP) ? nextP : null;
                        }

                        // Eğer pathList'in sonunda proje adı yoksa ekle (Direct paketlerde)
                        if (!pathList.Contains(rootProjectName))
                        {
                            pathList.Add(rootProjectName);
                        }

                        pathList.Reverse();
                        var fullPath = string.Join(" -> ", pathList);


                        components.Add(new Component
                        {
                            Id = Guid.NewGuid(),
                            ScanId = scanId,
                            Name = name,
                            Version = version,
                            Purl = $"pkg:nuget/{name}@{version}",
                            IsTransitive = !isDirect,
                            ParentName = isDirect ? null : (parentMap.TryGetValue(name, out var parent) ? parent : null), // Beni doğrudan getiren
                            DependencyPath = fullPath, // Tam soyağacım (Örn: MyProject -> CycloneDX -> Protobuf)
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
            }

            return components;
        }
    }
}
