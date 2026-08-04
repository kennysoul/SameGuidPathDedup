using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;

namespace SameGuidPathDedup.Models
{
    /// <summary>
    /// A cluster of MediaItems rows that share the same Path.
    ///
    /// Holds both the live <see cref="BaseItem"/> references (needed by
    /// <c>ILibraryManager.DeleteItem</c>) and a serializable
    /// <see cref="DedupCandidate"/> snapshot used for log lines and REST
    /// responses.
    ///
    /// Detection: same Path AND &gt;1 rows. On the live server this happens
    /// only when Emby's identification flow creates a parallel DB row for the
    /// same logical item (e.g. one row with English filename-derived name,
    /// one row with Chinese TMDB-derived name). Legitimate multi-version files
    /// have DIFFERENT Paths (different filenames), so they are never matched.
    /// </summary>
    public class DedupGroup
    {
        public string Path { get; set; } = "";

        /// <summary>The row we will KEEP (live reference).</summary>
        public BaseItem KeepItem { get; set; }

        /// <summary>The rows we will DELETE (live references).</summary>
        public List<BaseItem> DeleteItems { get; set; } = new List<BaseItem>();

        /// <summary>Serializable snapshot of <see cref="KeepItem"/>, used for logs / REST.</summary>
        public DedupCandidate Keep => KeepItem == null ? null : DedupCandidate.From(KeepItem);

        /// <summary>Serializable snapshots of <see cref="DeleteItems"/>, used for logs / REST.</summary>
        public List<DedupCandidate> Delete =>
            DeleteItems == null
                ? new List<DedupCandidate>()
                : DeleteItems.Select(DedupCandidate.From).ToList();
    }

    /// <summary>
    /// Result of one dedup pass. Surfaced via the REST endpoint and the
    /// Emby log. Kept deliberately simple — admins read this in plain text.
    /// </summary>
    public class DedupReport
    {
        public string Source { get; set; } = "";
        public DateTime StartedAt { get; set; }
        public DateTime FinishedAt { get; set; }
        public int GroupsFound { get; set; }
        public int ItemsDeleted { get; set; }
        public int ItemsFailed { get; set; }
        public List<DedupGroup> Groups { get; set; } = new List<DedupGroup>();

        public TimeSpan Duration => FinishedAt - StartedAt;
    }
}