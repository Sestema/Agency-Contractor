using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Views;

namespace Win11DesktopApp.Services;

public sealed class AiWindowFactory
{
    private readonly GeminiApiService _geminiApiService;
    private readonly EmployeeService _employeeService;

    public AiWindowFactory(GeminiApiService geminiApiService, EmployeeService employeeService)
    {
        _geminiApiService = geminiApiService;
        _employeeService = employeeService;
    }

    public AITemplateOverlayWindow CreateTemplateOverlayWindow()
    {
        return new AITemplateOverlayWindow(_geminiApiService);
    }

    public ReplaceDocumentWindow CreateReplaceDocumentWindow(
        string docType,
        EmployeeData data,
        string? existingFilePath = null,
        bool editCurrent = false)
    {
        return new ReplaceDocumentWindow(docType, data, _geminiApiService, _employeeService, existingFilePath, editCurrent);
    }
}
