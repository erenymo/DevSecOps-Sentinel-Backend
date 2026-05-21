using System;
using System.Collections.Generic;
using System.Linq;

namespace Sentinel.Application.Common
{
    /// <summary>
    /// Semantic versioning (SemVer) yardımcı sınıfı.
    /// Kullanıcının major/minor sürüm hattını kırmayacak şekilde en uygun yama sürümünü bulur.
    /// </summary>
    public static class SemVerHelper
    {
        /// <summary>
        /// Verilen sürümler listesinden, referans sürümün (currentVersion) major.minor
        /// hattına denk gelen en yüksek patch sürümünü seçer.
        ///
        /// Örnek:
        ///   currentVersion = "4.17.15"
        ///   candidates = ["3.10.0", "4.17.21", "5.0.0"]
        ///   → Döner: "4.17.21"  (major=4, minor=17 korunur, patch=21 > 15)
        ///
        /// Eğer aynı major.minor hattında hiç uygun sürüm yoksa, tüm adaylar içinden
        /// en yüksek semantik sürümü döner (fallback).
        /// </summary>
        public static string SelectBestPatch(string currentVersion, IEnumerable<string> candidates)
        {
            if (string.IsNullOrWhiteSpace(currentVersion))
                return SelectMaxVersion(candidates);

            if (!TryParse(currentVersion, out int curMajor, out int curMinor, out _))
                return SelectMaxVersion(candidates);

            // Aynı major.minor hattındaki adaylar
            var sameLane = candidates
                .Where(c => TryParse(c, out int maj, out int min, out _) && maj == curMajor && min == curMinor)
                .OrderByDescending(c =>
                {
                    TryParse(c, out _, out _, out int patch);
                    return patch;
                })
                .ToList();

            if (sameLane.Any())
                return sameLane.First();

            // Fallback: herhangi bir uygun yama yok, tüm listeden en yükseği dön
            return SelectMaxVersion(candidates);
        }

        /// <summary>
        /// Verilen sürümleri semantik olarak artan sırada sıralar.
        /// </summary>
        public static List<string> SortVersions(IEnumerable<string> versions)
        {
            return versions
                .Where(v => TryParse(v, out _, out _, out _))
                .OrderBy(v =>
                {
                    TryParse(v, out int maj, out int min, out int patch);
                    return (maj, min, patch);
                }, Comparer<(int, int, int)>.Create((a, b) =>
                {
                    if (a.Item1 != b.Item1) return a.Item1.CompareTo(b.Item1);
                    if (a.Item2 != b.Item2) return a.Item2.CompareTo(b.Item2);
                    return a.Item3.CompareTo(b.Item3);
                }))
                .Union(versions.Where(v => !TryParse(v, out _, out _, out _)))
                .ToList();
        }

        /// <summary>
        /// Verilen sürümler listesinden en yüksek semantik sürümü seçer.
        /// </summary>
        public static string SelectMaxVersion(IEnumerable<string> candidates)
        {
            return candidates
                .Where(c => TryParse(c, out _, out _, out _))
                .OrderByDescending(c =>
                {
                    TryParse(c, out int maj, out int min, out int patch);
                    return (maj, min, patch);
                }, Comparer<(int, int, int)>.Create((a, b) =>
                {
                    if (a.Item1 != b.Item1) return a.Item1.CompareTo(b.Item1);
                    if (a.Item2 != b.Item2) return a.Item2.CompareTo(b.Item2);
                    return a.Item3.CompareTo(b.Item3);
                }))
                .FirstOrDefault() ?? string.Empty;
        }

        /// <summary>
        /// Bir sürüm stringini major.minor.patch olarak parse eder.
        /// Önce "v" öneki ve pre-release etiketleri temizlenir.
        /// </summary>
        public static bool TryParse(string version, out int major, out int minor, out int patch)
        {
            major = 0;
            minor = 0;
            patch = 0;

            if (string.IsNullOrWhiteSpace(version))
                return false;

            // "v1.2.3", "1.2.3-beta", "1.2.3+build" gibi formatları temizle
            var cleaned = version.TrimStart('v', 'V')
                                 .Split('-')[0]
                                 .Split('+')[0]
                                 .Trim();

            var parts = cleaned.Split('.');
            if (parts.Length < 1) return false;

            if (!int.TryParse(parts[0], out major)) return false;
            if (parts.Length >= 2) int.TryParse(parts[1], out minor);
            if (parts.Length >= 3) int.TryParse(parts[2], out patch);

            return true;
        }
    }
}
