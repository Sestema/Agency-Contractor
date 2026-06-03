using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services.Scanning
{
    public interface IScanSessionStore : IDisposable
    {
        string SessionFolder { get; }

        ScanPage AddPageFromFile(string sourcePath);

        IReadOnlyList<ScanPage> AddPagesFromFile(string sourcePath);

        void ReplacePageFile(ScanPage page, string newPath);

        void RemovePage(ScanPage page);

        void Reorder(IReadOnlyList<ScanPage> orderedPages);

        void Cleanup();
    }
}
