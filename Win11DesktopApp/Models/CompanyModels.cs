using System.Collections.Generic;
using System.Collections.ObjectModel;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Models
{
    public class EmployerCompany : ViewModelBase
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastModified { get; set; } = DateTime.Now;

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private string _ico = string.Empty;
        public string ICO
        {
            get => _ico;
            set => SetProperty(ref _ico, value);
        }

        public ObservableCollection<WorkAddress> Addresses { get; set; } = new();
        public ObservableCollection<Position> Positions { get; set; } = new();
        
        // Agency data (stored together with employer)
        public AgencyCompany Agency { get; set; } = new();

        // Tags dictionary (Key: FieldName, Value: Tag)
        public Dictionary<string, string> Tags { get; set; } = new();
    }

    public class WorkAddress : ViewModelBase
    {
        private string _street = string.Empty;
        public string Street { get => _street; set => SetProperty(ref _street, value); }

        private string _number = string.Empty;
        public string Number { get => _number; set => SetProperty(ref _number, value); }

        private string _city = string.Empty;
        public string City { get => _city; set => SetProperty(ref _city, value); }

        private string _zipCode = string.Empty;
        public string ZipCode { get => _zipCode; set => SetProperty(ref _zipCode, value); }
    }

    public class Position : ViewModelBase
    {
        private string _title = string.Empty;
        public string Title { get => _title; set => SetProperty(ref _title, value); }

        private string _positionNumber = string.Empty;
        public string PositionNumber { get => _positionNumber; set => SetProperty(ref _positionNumber, value); }

        private decimal _monthlySalaryBrutto;
        public decimal MonthlySalaryBrutto { get => _monthlySalaryBrutto; set => SetProperty(ref _monthlySalaryBrutto, value); }

        private decimal _hourlySalary;
        public decimal HourlySalary { get => _hourlySalary; set => SetProperty(ref _hourlySalary, value); }
    }

    public class AgencyCompany : ViewModelBase
    {
        private string _name = string.Empty;
        public string Name { get => _name; set => SetProperty(ref _name, value); }

        private string _ico = string.Empty;
        public string ICO { get => _ico; set => SetProperty(ref _ico, value); }

        private string _fullAddress = string.Empty;
        public string FullAddress { get => _fullAddress; set => SetProperty(ref _fullAddress, value); }
        
        public Dictionary<string, string> Tags { get; set; } = new();
    }

    public class TagEntry
    {
        public string Tag { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // Employer, Address, Position, Agency
        public string CompanyName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty; // Actual value of the field
    }
}
