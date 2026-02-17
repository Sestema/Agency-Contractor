using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Win11DesktopApp.Models;
using EmployeeModels = Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class AddEmployeeWizardViewModel : ViewModelBase
    {
        private readonly EmployeeService _employeeService;
        private readonly EmployerCompany _company;
        private readonly string _tempFolder;

        public event Action? RequestClose;

        private int _stepIndex;
        public int StepIndex
        {
            get => _stepIndex;
            set => SetProperty(ref _stepIndex, value);
        }

        public EmployeeModels.EmployeeData Data { get; } = new EmployeeModels.EmployeeData();

        public ObservableCollection<WorkAddress> CompanyAddresses { get; }
        public ObservableCollection<Position> CompanyPositions { get; }

        private WorkAddress? _selectedWorkAddress;
        public WorkAddress? SelectedWorkAddress
        {
            get => _selectedWorkAddress;
            set => SetProperty(ref _selectedWorkAddress, value);
        }

        private Position? _selectedPosition;
        public Position? SelectedPosition
        {
            get => _selectedPosition;
            set => SetProperty(ref _selectedPosition, value);
        }

        private string _contractType = "HPP";
        public string ContractType
        {
            get => _contractType;
            set => SetProperty(ref _contractType, value);
        }

        public EmployeeModels.EmployeeDocumentTemp PassportDoc { get; private set; } = new EmployeeModels.EmployeeDocumentTemp();
        public EmployeeModels.EmployeeDocumentTemp VisaDoc { get; private set; } = new EmployeeModels.EmployeeDocumentTemp();
        public EmployeeModels.EmployeeDocumentTemp InsuranceDoc { get; private set; } = new EmployeeModels.EmployeeDocumentTemp();

        private string _passportPreviewPath = string.Empty;
        public string PassportPreviewPath
        {
            get => _passportPreviewPath;
            set => SetProperty(ref _passportPreviewPath, value);
        }

        private string _visaPreviewPath = string.Empty;
        public string VisaPreviewPath
        {
            get => _visaPreviewPath;
            set => SetProperty(ref _visaPreviewPath, value);
        }

        private string _insurancePreviewPath = string.Empty;
        public string InsurancePreviewPath
        {
            get => _insurancePreviewPath;
            set => SetProperty(ref _insurancePreviewPath, value);
        }

        private string _croppedPhotoPath = string.Empty;
        public string CroppedPhotoPath
        {
            get => _croppedPhotoPath;
            set => SetProperty(ref _croppedPhotoPath, value);
        }

        private Int32Rect _cropRect = new Int32Rect(0, 0, 200, 200);
        public Int32Rect CropRect
        {
            get => _cropRect;
            set => SetProperty(ref _cropRect, value);
        }

        public ICommand NextCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SaveCommand { get; }

        public ICommand UploadPassportCommand { get; }
        public ICommand UploadVisaCommand { get; }
        public ICommand UploadInsuranceCommand { get; }
        public ICommand ApplyCropCommand { get; }

        public AddEmployeeWizardViewModel(EmployerCompany company, EmployeeService? employeeService = null)
        {
            _company = company;
            _employeeService = employeeService ?? App.EmployeeService;
            _tempFolder = _employeeService.CreateTempFolder();

            CompanyAddresses = company.Addresses;
            CompanyPositions = company.Positions;

            NextCommand = new RelayCommand(o => StepIndex++, o => StepIndex < 6);
            BackCommand = new RelayCommand(o => StepIndex--, o => StepIndex > 0);
            CancelCommand = new RelayCommand(o => Close());
            SaveCommand = new RelayCommand(o => SaveEmployee());

            UploadPassportCommand = new RelayCommand(o => UploadDocument("passport"));
            UploadVisaCommand = new RelayCommand(o => UploadDocument("visa"));
            UploadInsuranceCommand = new RelayCommand(o => UploadDocument("insurance"));
            ApplyCropCommand = new RelayCommand(o => ApplyCrop());
        }

        private void UploadDocument(string type)
        {
            var dialog = new OpenFileDialog();
            dialog.Filter = "Documents|*.jpg;*.jpeg;*.png;*.heic;*.pdf";
            if (dialog.ShowDialog() != true) return;

            try
            {
                var temp = _employeeService.PrepareTempDocument(dialog.FileName, _tempFolder, type);
                if (type == "passport")
                {
                    PassportDoc = temp;
                    PassportPreviewPath = temp.IsPdf ? string.Empty : temp.ImagePath;
                    OnPropertyChanged(nameof(PassportDoc));
                }
                if (type == "visa")
                {
                    VisaDoc = temp;
                    VisaPreviewPath = temp.IsPdf ? string.Empty : temp.ImagePath;
                    OnPropertyChanged(nameof(VisaDoc));
                }
                if (type == "insurance")
                {
                    InsuranceDoc = temp;
                    InsurancePreviewPath = temp.IsPdf ? string.Empty : temp.ImagePath;
                    OnPropertyChanged(nameof(InsuranceDoc));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyCrop()
        {
            if (string.IsNullOrEmpty(PassportPreviewPath))
            {
                MessageBox.Show("Паспорт не завантажено або доступний лише PDF.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dest = System.IO.Path.Combine(_tempFolder, "employee_photo.jpg");
            try
            {
                _employeeService.CreateCroppedPhoto(PassportPreviewPath, CropRect, dest);
                CroppedPhotoPath = dest;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveEmployee()
        {
            Data.WorkAddressTag = SelectedWorkAddress != null
                ? $"{SelectedWorkAddress.Street} {SelectedWorkAddress.Number}, {SelectedWorkAddress.City} {SelectedWorkAddress.ZipCode}"
                : string.Empty;
            Data.PositionTag = SelectedPosition?.Title ?? string.Empty;
            Data.PositionNumber = SelectedPosition?.PositionNumber ?? string.Empty;
            Data.MonthlySalaryBrutto = SelectedPosition?.MonthlySalaryBrutto ?? 0;
            Data.HourlySalary = SelectedPosition?.HourlySalary ?? 0;
            Data.ContractType = ContractType;

            var folder = _employeeService.SaveEmployee(_company.Name, Data, PassportDoc, VisaDoc, InsuranceDoc, CroppedPhotoPath);
            if (string.IsNullOrEmpty(folder))
            {
                MessageBox.Show("Не вдалося зберегти працівника.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Close();
        }

        private void Close()
        {
            _employeeService.CleanupTempFolder(_tempFolder);
            RequestClose?.Invoke();
        }
    }
}
