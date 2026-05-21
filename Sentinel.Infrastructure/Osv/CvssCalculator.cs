using System;
using System.Collections.Generic;

namespace Sentinel.Infrastructure.Osv
{
    public static class CvssCalculator
    {
        public static (decimal Score, string Level) CalculateCvssV4(string vector)
        {
            if (string.IsNullOrWhiteSpace(vector))
                return (0m, "NONE");

            var cleanVector = vector;
            if (cleanVector.StartsWith("CVSS:4", StringComparison.OrdinalIgnoreCase))
            {
                cleanVector = cleanVector.Substring(9);
            }

            var metrics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parts = cleanVector.Split('/');

            foreach (var part in parts)
            {
                var kv = part.Split(':');
                if (kv.Length == 2)
                {
                    metrics[kv[0]] = kv[1];
                }
            }

            // Impact metrics (Vulnerable System)
            var vc = metrics.GetValueOrDefault("VC", "N") switch { "H" => 0.56m, "L" => 0.22m, "N" => 0.0m, _ => 0.0m };
            var vi = metrics.GetValueOrDefault("VI", "N") switch { "H" => 0.56m, "L" => 0.22m, "N" => 0.0m, _ => 0.0m };
            var va = metrics.GetValueOrDefault("VA", "N") switch { "H" => 0.56m, "L" => 0.22m, "N" => 0.0m, _ => 0.0m };

            // Impact metrics (Subsequent System)
            var sc = metrics.GetValueOrDefault("SC", "N") switch { "H" => 0.45m, "L" => 0.18m, "N" => 0.0m, _ => 0.0m };
            var si = metrics.GetValueOrDefault("SI", "N") switch { "H" => 0.45m, "L" => 0.18m, "N" => 0.0m, _ => 0.0m };
            var sa = metrics.GetValueOrDefault("SA", "N") switch { "H" => 0.45m, "L" => 0.18m, "N" => 0.0m, _ => 0.0m };

            var iss = 1m - ((1m - vc) * (1m - vi) * (1m - va) * (1m - sc) * (1m - si) * (1m - sa));

            if (iss <= 0m)
                return (0m, "NONE");

            // Normalize impact against max possible ISS (approx 0.985)
            var impact = 10.0m * (iss / 0.985m);

            // Exploitability metrics
            // Attack Vector (AV): Network (N) => 1.0, Adjacent (A) => 0.8, Local (L) => 0.5, Physical (P) => 0.2
            var av = metrics.GetValueOrDefault("AV", "N") switch { "N" => 1.0m, "A" => 0.8m, "L" => 0.5m, "P" => 0.2m, _ => 1.0m };
            // Attack Complexity (AC): Low (L) => 1.0, High (H) => 0.6
            var ac = metrics.GetValueOrDefault("AC", "L") switch { "L" => 1.0m, "H" => 0.6m, _ => 1.0m };
            // Attack Requirements (AT): None (N) => 1.0, Present (P) => 0.7
            var at = metrics.GetValueOrDefault("AT", "N") switch { "N" => 1.0m, "P" => 0.7m, _ => 1.0m };
            // Privileges Required (PR): None (N) => 1.0, Low (L) => 0.8, High (H) => 0.5
            var pr = metrics.GetValueOrDefault("PR", "N") switch { "N" => 1.0m, "L" => 0.8m, "H" => 0.5m, _ => 1.0m };
            // User Interaction (UI): None (N) => 1.0, Passive (P) => 0.8, Active (A) => 0.5
            var ui = metrics.GetValueOrDefault("UI", "N") switch { "N" => 1.0m, "P" => 0.8m, "A" => 0.5m, _ => 1.0m };

            var exploitability = av * ac * at * pr * ui;

            var score = impact * exploitability;
            score = Math.Min(10m, Math.Max(0m, score));
            score = Math.Round(score, 1);

            string level = score switch
            {
                0.0m => "NONE",
                >= 0.1m and <= 3.9m => "LOW",
                >= 4.0m and <= 6.9m => "MEDIUM",
                >= 7.0m and <= 8.9m => "HIGH",
                >= 9.0m and <= 10.0m => "CRITICAL",
                _ => "UNKNOWN"
            };

            return (score, level);
        }

        public static (decimal Score, string Level) CalculateCvssV3(string vector)
        {
            if (string.IsNullOrWhiteSpace(vector))
                return (0m, "NONE");

            var cleanVector = vector;
            if (cleanVector.StartsWith("CVSS:3.1/", StringComparison.OrdinalIgnoreCase))
            {
                cleanVector = cleanVector.Substring(9);
            }
            else if (cleanVector.StartsWith("CVSS:3.0/", StringComparison.OrdinalIgnoreCase))
            {
                cleanVector = cleanVector.Substring(9);
            }

            var metrics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parts = cleanVector.Split('/');

            foreach (var part in parts)
            {
                var kv = part.Split(':');
                if (kv.Length == 2)
                {
                    metrics[kv[0]] = kv[1];
                }
            }

            var av = metrics.GetValueOrDefault("AV", "N") switch { "N" => 0.85m, "A" => 0.62m, "L" => 0.55m, "P" => 0.2m, _ => 0.85m };
            var ac = metrics.GetValueOrDefault("AC", "L") switch { "L" => 0.77m, "H" => 0.44m, _ => 0.77m };
            var pr = metrics.GetValueOrDefault("PR", "N") switch { "N" => 0.85m, "L" => metrics.GetValueOrDefault("S") == "C" ? 0.68m : 0.62m, "H" => metrics.GetValueOrDefault("S") == "C" ? 0.50m : 0.27m, _ => 0.85m };
            var ui = metrics.GetValueOrDefault("UI", "N") switch { "N" => 0.85m, "R" => 0.62m, _ => 0.85m };

            var exploitability = 8.22m * av * ac * pr * ui;

            var c = metrics.GetValueOrDefault("C", "N") switch { "H" => 0.56m, "L" => 0.22m, "N" => 0m, _ => 0m };
            var i = metrics.GetValueOrDefault("I", "N") switch { "H" => 0.56m, "L" => 0.22m, "N" => 0m, _ => 0m };
            var a = metrics.GetValueOrDefault("A", "N") switch { "H" => 0.56m, "L" => 0.22m, "N" => 0m, _ => 0m };

            var iss = 1m - ((1m - c) * (1m - i) * (1m - a));
            var scope = metrics.GetValueOrDefault("S", "U");

            decimal impact = 0m;
            if (scope == "U")
                impact = 6.42m * iss;
            else
                impact = 7.52m * (iss - 0.029m) - 3.25m * (decimal)Math.Pow((double)(iss - 0.02m), 15);

            decimal score = 0m;
            if (impact <= 0)
                score = 0m;
            else if (scope == "U")
                score = Math.Min(impact + exploitability, 10m);
            else
                score = Math.Min(1.08m * (impact + exploitability), 10m);

            score = Math.Ceiling(score * 10m) / 10m; // Roundup

            string level = score switch
            {
                0.0m => "NONE",
                >= 0.1m and <= 3.9m => "LOW",
                >= 4.0m and <= 6.9m => "MEDIUM",
                >= 7.0m and <= 8.9m => "HIGH",
                >= 9.0m and <= 10.0m => "CRITICAL",
                _ => "UNKNOWN"
            };

            return (score, level);
        }

        public static (decimal Score, string Level) CalculateCvssV2(string vector)
        {
            if (string.IsNullOrWhiteSpace(vector))
                return (0m, "NONE");

            var cleanVector = vector;
            if (cleanVector.StartsWith("CVSS:2.0/", StringComparison.OrdinalIgnoreCase))
            {
                cleanVector = cleanVector.Substring(9);
            }

            var metrics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parts = cleanVector.Split('/');

            foreach (var part in parts)
            {
                var kv = part.Split(':');
                if (kv.Length == 2)
                {
                    metrics[kv[0]] = kv[1];
                }
            }

            // Exploitability metrics
            var av = metrics.GetValueOrDefault("AV", "N") switch { "L" => 0.395m, "A" => 0.646m, "N" => 1.000m, _ => 1.000m };
            var ac = metrics.GetValueOrDefault("AC", "L") switch { "H" => 0.35m, "M" => 0.61m, "L" => 0.71m, _ => 0.71m };
            var au = metrics.GetValueOrDefault("Au", "N") switch { "M" => 0.45m, "S" => 0.56m, "N" => 0.704m, _ => 0.704m };

            var exploitability = 20m * av * ac * au;

            // Impact metrics
            var c = metrics.GetValueOrDefault("C", "N") switch { "N" => 0.0m, "P" => 0.275m, "C" => 0.660m, _ => 0m };
            var i = metrics.GetValueOrDefault("I", "N") switch { "N" => 0.0m, "P" => 0.275m, "C" => 0.660m, _ => 0m };
            var a = metrics.GetValueOrDefault("A", "N") switch { "N" => 0.0m, "P" => 0.275m, "C" => 0.660m, _ => 0m };

            var impact = 10.41m * (1m - ((1m - c) * (1m - i) * (1m - a)));

            decimal score = 0m;
            if (impact > 0m)
            {
                var fImpact = 1.176m;
                var baseScore = ((0.6m * impact) + (0.4m * exploitability) - 1.5m) * fImpact;
                score = Math.Min(10m, Math.Max(0m, baseScore));
                score = Math.Round(score, 1);
            }

            string level = score switch
            {
                0.0m => "NONE",
                >= 0.1m and <= 3.9m => "LOW",
                >= 4.0m and <= 6.9m => "MEDIUM",
                >= 7.0m and <= 8.9m => "HIGH",
                >= 9.0m and <= 10.0m => "CRITICAL",
                _ => "UNKNOWN"
            };

            return (score, level);
        }
    }
}
