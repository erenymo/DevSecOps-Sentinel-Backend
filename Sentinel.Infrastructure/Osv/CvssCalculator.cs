using System;
using System.Collections.Generic;

namespace Sentinel.Infrastructure.Osv
{
    public static class CvssCalculator
    {
        public static (decimal Score, string Level) CalculateCvssV3(string vector)
        {
            if (string.IsNullOrWhiteSpace(vector) || !vector.StartsWith("CVSS:3.1/"))
                return (0m, "NONE");

            var metrics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parts = vector.Substring(9).Split('/');
            
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
    }
}
