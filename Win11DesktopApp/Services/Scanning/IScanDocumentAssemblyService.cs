using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services.Scanning
{
    public interface IScanDocumentAssemblyService
    {
        Task<string> ExportAsync(
            IReadOnlyList<string> pagePaths,
            string outputFolder,
            ScanExportOptions options,
            CancellationToken cancellationToken = default);
    }
}
