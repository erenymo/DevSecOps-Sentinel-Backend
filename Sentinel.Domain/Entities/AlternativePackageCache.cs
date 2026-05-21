using System;

namespace Sentinel.Domain.Entities
{
    /// <summary>
    /// Smart Cache: AI tarafından üretilen alternatif paket ve VEX önerilerini 30 gün boyunca saklar.
    /// PackageName Primary Key olarak kullanılır, böylece aynı paket için tekrar OpenAI'a gidilmez.
    /// </summary>
    public class AlternativePackageCache
    {
        /// <summary>
        /// Paket adı — Primary Key (örn: "lodash", "axios")
        /// </summary>
        public string PackageName { get; set; } = string.Empty;

        /// <summary>
        /// AI'ın bu paket için ürettiği tam analiz çıktısı (JSON).
        /// İçeriği: aiTriageComment, recommendedVexStatus, alternatives[]
        /// </summary>
        public string ResultJson { get; set; } = "{}";

        /// <summary>
        /// Cache'in son güncellenme tarihi. TTL kontrolü için kullanılır.
        /// 30 günden eski kayıtlar geçersiz sayılır ve AI'a tekrar gidilir.
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
