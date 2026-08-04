using System;

namespace SameGuidPathDedup.Models
{
    /// <summary>
    /// Lightweight snapshot of a single item that is a deletion candidate
    /// (or the row we chose to keep) inside a duplicate group.
    /// </summary>
    public class DedupCandidate
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public Guid Guid { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
        public bool HasProviderIds { get; set; }
        public int? ProviderCount { get; set; }

        public static DedupCandidate From(MediaBrowser.Controller.Entities.BaseItem item)
        {
            int? providerCount = null;
            try
            {
                if (item.ProviderIds != null)
                {
                    providerCount = item.ProviderIds.Count;
                }
            }
            catch { /* ProviderIds may not be enumerated on every leaf type */ }

            return new DedupCandidate
            {
                Id = item.Id,
                Name = item.Name ?? "",
                Path = item.Path ?? "",
                Guid = item.Guid,
                DateCreated = item.DateCreated,
                DateModified = item.DateModified,
                HasProviderIds = providerCount.HasValue && providerCount.Value > 0,
                ProviderCount = providerCount
            };
        }
    }
}