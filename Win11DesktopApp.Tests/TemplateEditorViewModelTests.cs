using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class TemplateEditorViewModelTests : IDisposable
    {
        private readonly string _testRootPath;
        private readonly AppSettingsService _appSettingsService;
        private readonly TemplateService _templateService;
        private readonly TagCatalogService _tagCatalogService;
        private readonly DocumentLocalizationService _documentLocalizationService;
        private readonly NavigationService _navigationService;
        private readonly CompanyService _companyService;
        private readonly GeminiApiService _geminiApiService;
        private readonly StarterTemplateCatalogService _starterTemplateCatalogService;
        private readonly TemplateViewModelFactory _templateViewModelFactory;
        private readonly CurrentProfileService _currentProfileService;
        private readonly AiWindowFactory _aiWindowFactory;

        public TemplateEditorViewModelTests()
        {
            _testRootPath = Path.Combine(Path.GetTempPath(), "EditorTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testRootPath);

            _appSettingsService = new AppSettingsService();
            _appSettingsService.Settings.RootFolderPath = _testRootPath;
            _appSettingsService.Settings.LanguageCode = "en";

            var folderService = new FolderService(_appSettingsService);
            _templateService = new TemplateService(_appSettingsService, folderService);
            _documentLocalizationService = new DocumentLocalizationService();
            _tagCatalogService = new TagCatalogService(_documentLocalizationService);
            _navigationService = new NavigationService(new ServiceCollection().BuildServiceProvider());
            _currentProfileService = new CurrentProfileService();
            _companyService = new CompanyService(_tagCatalogService, _appSettingsService, new PersistenceService(_appSettingsService, folderService), folderService);
            _geminiApiService = new GeminiApiService();
            _starterTemplateCatalogService = new StarterTemplateCatalogService();
            _aiWindowFactory = new AiWindowFactory(
                _geminiApiService,
                new EmployeeService(_appSettingsService, _tagCatalogService, folderService, currentProfileService: _currentProfileService));
            _templateViewModelFactory = new TemplateViewModelFactory(
                _templateService,
                new ActivityLogService(folderService, currentProfileService: _currentProfileService),
                _navigationService,
                _companyService,
                _geminiApiService,
                _tagCatalogService,
                _appSettingsService,
                _starterTemplateCatalogService,
                _aiWindowFactory);
        }

        [Fact]
        public void Constructor_ShouldSetRtfFilePath()
        {
            var firmName = "TestFirm";
            var fileName = "test.docx";
            SetupTemplateFile(firmName, fileName, "dummy content");

            var template = new TemplateEntry { FilePath = $"Templates/Test_Template/{fileName}" };

            var vm = new TemplateEditorViewModel(
                firmName,
                template,
                _tagCatalogService,
                _templateService,
                _navigationService,
                _templateViewModelFactory,
                _companyService,
                _geminiApiService,
                _starterTemplateCatalogService,
                _appSettingsService,
                _aiWindowFactory);

            Assert.False(string.IsNullOrEmpty(vm.RtfFilePath));
            Assert.EndsWith("content.rtf", vm.RtfFilePath);
            Assert.EndsWith("content.xamlpackage", vm.NativeDocumentPath);
        }

        [Fact]
        public void Constructor_ShouldPopulateTagGroups()
        {
            var firmName = "TestFirm";
            var fileName = "test.docx";
            SetupTemplateFile(firmName, fileName, "dummy content");

            var template = new TemplateEntry { FilePath = $"Templates/Test_Template/{fileName}" };

            var vm = new TemplateEditorViewModel(
                firmName,
                template,
                _tagCatalogService,
                _templateService,
                _navigationService,
                _templateViewModelFactory,
                _companyService,
                _geminiApiService,
                _starterTemplateCatalogService,
                _appSettingsService,
                _aiWindowFactory);

            Assert.NotNull(vm.TagGroups);
        }

        [Fact]
        public void TagSearchQuery_ShouldFilterTagGroups()
        {
            var firmName = "TestFirm";
            var fileName = "test.docx";
            SetupTemplateFile(firmName, fileName, "dummy content");

            var template = new TemplateEntry { FilePath = $"Templates/Test_Template/{fileName}" };

            var vm = new TemplateEditorViewModel(
                firmName,
                template,
                _tagCatalogService,
                _templateService,
                _navigationService,
                _templateViewModelFactory,
                _companyService,
                _geminiApiService,
                _starterTemplateCatalogService,
                _appSettingsService,
                _aiWindowFactory);

            vm.TagSearchQuery = "EMPLOYEE";

            Assert.NotNull(vm.FilteredTagGroups);
        }

        [Fact]
        public void Constructor_ShouldHandleMissingTemplate()
        {
            var firmName = "TestFirm";
            var template = new TemplateEntry { FilePath = "Templates/Missing/missing.docx" };

            var vm = new TemplateEditorViewModel(
                firmName,
                template,
                _tagCatalogService,
                _templateService,
                _navigationService,
                _templateViewModelFactory,
                _companyService,
                _geminiApiService,
                _starterTemplateCatalogService,
                _appSettingsService,
                _aiWindowFactory);

            Assert.NotNull(vm.TagGroups);
        }

        [Fact]
        public void Constructor_ShouldRestorePersistedPageLayout()
        {
            var firmName = "TestFirm";
            var fileName = "test.docx";
            SetupTemplateFile(firmName, fileName, "dummy content");

            var folder = Path.Combine(_testRootPath, firmName, "Templates", "Test_Template");
            SafeFileService.WriteJsonAtomic(Path.Combine(folder, "editor-layout.json"), new TemplateEditorLayoutSettings
            {
                PageSizeKey = "letter",
                OrientationKey = "landscape",
                MarginKey = "wide"
            });

            var template = new TemplateEntry { FilePath = $"Templates/Test_Template/{fileName}" };

            var vm = new TemplateEditorViewModel(
                firmName,
                template,
                _tagCatalogService,
                _templateService,
                _navigationService,
                _templateViewModelFactory,
                _companyService,
                _geminiApiService,
                _starterTemplateCatalogService,
                _appSettingsService,
                _aiWindowFactory);

            Assert.Equal("letter", vm.SelectedPageSize?.Key);
            Assert.Equal("landscape", vm.SelectedPageOrientation?.Key);
            Assert.Equal("wide", vm.SelectedPageMargin?.Key);
        }

        [Fact]
        public async Task SaveCommand_ShouldPersistPageLayout()
        {
            var firmName = "TestFirm";
            var fileName = "test.docx";
            SetupTemplateFile(firmName, fileName, "dummy content");

            var template = new TemplateEntry { FilePath = $"Templates/Test_Template/{fileName}" };
            var vm = new TemplateEditorViewModel(
                firmName,
                template,
                _tagCatalogService,
                _templateService,
                _navigationService,
                _templateViewModelFactory,
                _companyService,
                _geminiApiService,
                _starterTemplateCatalogService,
                _appSettingsService,
                _aiWindowFactory)
            {
                RequestGetRtfContent = () => "{\\rtf1\\ansi test}"
            };
            vm.NotifyEditorLoaded();

            vm.SelectedPageSize = vm.AvailablePageSizes.Single(x => x.Key == "letter");
            vm.SelectedPageOrientation = vm.AvailablePageOrientations.Single(x => x.Key == "landscape");
            vm.SelectedPageMargin = vm.AvailablePageMargins.Single(x => x.Key == "wide");

            vm.SaveCommand.Execute(null);
            await Task.Delay(250);

            var settings = SafeFileService.ReadJsonOrDefault(vm.LayoutSettingsPath, new TemplateEditorLayoutSettings());
            Assert.Equal("letter", settings.PageSizeKey);
            Assert.Equal("landscape", settings.OrientationKey);
            Assert.Equal("wide", settings.MarginKey);
        }

        [Fact]
        public async Task SaveCommand_ShouldPersistNativeEditorDocument()
        {
            var firmName = "TestFirm";
            var fileName = "test.docx";
            SetupTemplateFile(firmName, fileName, "dummy content");

            var template = new TemplateEntry { FilePath = $"Templates/Test_Template/{fileName}" };
            var expectedBytes = new byte[] { 1, 2, 3, 4, 5 };
            var vm = new TemplateEditorViewModel(
                firmName,
                template,
                _tagCatalogService,
                _templateService,
                _navigationService,
                _templateViewModelFactory,
                _companyService,
                _geminiApiService,
                _starterTemplateCatalogService,
                _appSettingsService,
                _aiWindowFactory)
            {
                RequestGetRtfContent = () => "{\\rtf1\\ansi test}",
                RequestGetXamlPackageContent = () => expectedBytes
            };
            vm.NotifyEditorLoaded();

            vm.SaveCommand.Execute(null);
            await Task.Delay(250);

            Assert.True(File.Exists(vm.NativeDocumentPath));
            Assert.Equal(expectedBytes, SafeFileService.ReadAllBytes(vm.NativeDocumentPath));
        }

        private string SetupTemplateFile(string firmName, string fileName, string content)
        {
            var folder = Path.Combine(_testRootPath, firmName, "Templates", "Test_Template");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, fileName);
            var ext = Path.GetExtension(fileName).ToLower();
            if (ext == ".docx" || ext == ".xlsx")
            {
                using var stream = File.Create(path);
                using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, false);
                var entry = archive.CreateEntry("dummy.txt");
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream);
                writer.Write(content);
            }
            else if (ext == ".pdf")
            {
                File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes("%PDF-1.4"));
            }
            else
            {
                File.WriteAllText(path, content);
            }
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(_testRootPath, true); } catch { }
        }
    }
}
