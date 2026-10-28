using System;

namespace Rapr
{
    public class VersionInfo
    {
        public required Version Version { get; set; }

        // WinGet updates have no download, and GitHub releases may lack a digest.
        public Uri? PageUrl { get; set; }
        public Uri? DownloadUrl { get; set; }
        public string? Sha256 { get; set; }
    }
}
