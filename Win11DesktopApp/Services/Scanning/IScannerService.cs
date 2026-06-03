using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services.Scanning
{
    public interface IScannerService
    {
        bool IsAvailable { get; }

        Task<IReadOnlyList<ScannerDeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default);

        Task<string> ScanToFileAsync(ScanSettings settings, string outputFolder, CancellationToken cancellationToken = default);

        Task<string> ScanViaDialogAsync(string outputFolder, CancellationToken cancellationToken = default);

        Task<ScannerDeviceInfo?> PickDeviceViaDialogAsync(CancellationToken cancellationToken = default);
    }
}
