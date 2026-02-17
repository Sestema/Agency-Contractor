using System.Collections.Generic;
using System.Linq;
using Win11DesktopApp.Models;
using EmployeeModels = Win11DesktopApp.EmployeeModels;

namespace Win11DesktopApp.Services
{
    public class TagCatalogService
    {
        private List<TagEntry> _tags = new List<TagEntry>();

        public void AddTagsForCompany(EmployerCompany employer, AgencyCompany agency)
        {
            AddTagsForEmployerOnly(employer);

            // Agency Tags
            AddTag("AGENCY_Name", "Agency", employer.Name, "Назва агентства", agency.Name);
            AddTag("AGENCY_ICO", "Agency", employer.Name, "ІЧО агентства", agency.ICO);
            AddTag("AGENCY_FullAddress", "Agency", employer.Name, "Повна адреса агентства", agency.FullAddress);
        }

        public void AddTagsForEmployerOnly(EmployerCompany employer)
        {
            AddTag("COMPANY_Name", "Company", employer.Name, "Назва фірми", employer.Name);
            AddTag("COMPANY_ICO", "Company", employer.Name, "ІЧО фірми", employer.ICO);

            for (int i = 0; i < employer.Addresses.Count; i++)
            {
                var addr = employer.Addresses[i];
                var idx = i + 1;
                AddTag($"COMPANY_ADDR{idx}_Street", "Company", employer.Name, $"Адреса {idx} - Вулиця", addr.Street);
                AddTag($"COMPANY_ADDR{idx}_Number", "Company", employer.Name, $"Адреса {idx} - Номер", addr.Number);
                AddTag($"COMPANY_ADDR{idx}_City", "Company", employer.Name, $"Адреса {idx} - Місто", addr.City);
                AddTag($"COMPANY_ADDR{idx}_Zip", "Company", employer.Name, $"Адреса {idx} - Індекс", addr.ZipCode);
                AddTag($"COMPANY_ADDR{idx}_Full", "Company", employer.Name, $"Адреса {idx} - Повна",
                    $"{addr.Street} {addr.Number}, {addr.City} {addr.ZipCode}");
            }

            for (int i = 0; i < employer.Positions.Count; i++)
            {
                var pos = employer.Positions[i];
                var idx = i + 1;
                AddTag($"COMPANY_POS{idx}_Title", "Company", employer.Name, $"Позиція {idx} - Назва", pos.Title);
                AddTag($"COMPANY_POS{idx}_Number", "Company", employer.Name, $"Позиція {idx} - Номер", pos.PositionNumber);
                AddTag($"COMPANY_POS{idx}_SalaryBrutto", "Company", employer.Name, $"Позиція {idx} - Зарплата (брутто)", pos.MonthlySalaryBrutto.ToString());
                AddTag($"COMPANY_POS{idx}_HourlySalary", "Company", employer.Name, $"Позиція {idx} - Годинна зарплата", pos.HourlySalary.ToString());
            }
        }

        public void AddTagsForEmployee(string companyName, EmployeeModels.EmployeeData data)
        {
            // Personal
            AddTag("EMPLOYEE_FirstName", "Employee", companyName, "Ім'я працівника", data.FirstName);
            AddTag("EMPLOYEE_LastName", "Employee", companyName, "Прізвище працівника", data.LastName);
            AddTag("EMPLOYEE_FullName", "Employee", companyName, "Повне ім'я працівника", $"{data.FirstName} {data.LastName}");
            AddTag("EMPLOYEE_BirthDate", "Employee", companyName, "Дата народження", data.BirthDate);

            // Passport
            AddTag("EMPLOYEE_PassportNumber", "Employee", companyName, "Номер паспорту", data.PassportNumber);
            AddTag("EMPLOYEE_PassportCity", "Employee", companyName, "Місто народження", data.PassportCity);
            AddTag("EMPLOYEE_PassportCountry", "Employee", companyName, "Країна народження", data.PassportCountry);
            AddTag("EMPLOYEE_PassportExpiry", "Employee", companyName, "Дата закінчення паспорту", data.PassportExpiry);

            // Visa
            AddTag("EMPLOYEE_VisaNumber", "Employee", companyName, "Номер візи", data.VisaNumber);
            AddTag("EMPLOYEE_VisaType", "Employee", companyName, "Тип візи", data.VisaType);
            AddTag("EMPLOYEE_VisaExpiry", "Employee", companyName, "Дата закінчення візи", data.VisaExpiry);

            // Insurance
            AddTag("EMPLOYEE_InsuranceCompany", "Employee", companyName, "Страхова компанія", data.InsuranceCompanyShort);
            AddTag("EMPLOYEE_InsuranceNumber", "Employee", companyName, "Номер страховки", data.InsuranceNumber);
            AddTag("EMPLOYEE_InsuranceExpiry", "Employee", companyName, "Дата закінчення страховки", data.InsuranceExpiry);

            // Local address
            AddTag("EMPLOYEE_LocalAddress_Street", "Employee", companyName, "Місцева адреса - Вулиця", data.AddressLocal.Street);
            AddTag("EMPLOYEE_LocalAddress_Number", "Employee", companyName, "Місцева адреса - Номер", data.AddressLocal.Number);
            AddTag("EMPLOYEE_LocalAddress_City", "Employee", companyName, "Місцева адреса - Місто", data.AddressLocal.City);
            AddTag("EMPLOYEE_LocalAddress_Zip", "Employee", companyName, "Місцева адреса - Індекс", data.AddressLocal.Zip);
            AddTag("EMPLOYEE_LocalAddress_Full", "Employee", companyName, "Місцева адреса - Повна",
                $"{data.AddressLocal.Street} {data.AddressLocal.Number}, {data.AddressLocal.City} {data.AddressLocal.Zip}");

            // Abroad address
            AddTag("EMPLOYEE_AbroadAddress_Street", "Employee", companyName, "Закордонна адреса - Вулиця", data.AddressAbroad.Street);
            AddTag("EMPLOYEE_AbroadAddress_Number", "Employee", companyName, "Закордонна адреса - Номер", data.AddressAbroad.Number);
            AddTag("EMPLOYEE_AbroadAddress_City", "Employee", companyName, "Закордонна адреса - Місто", data.AddressAbroad.City);
            AddTag("EMPLOYEE_AbroadAddress_Zip", "Employee", companyName, "Закордонна адреса - Індекс", data.AddressAbroad.Zip);
            AddTag("EMPLOYEE_AbroadAddress_Full", "Employee", companyName, "Закордонна адреса - Повна",
                $"{data.AddressAbroad.Street} {data.AddressAbroad.Number}, {data.AddressAbroad.City} {data.AddressAbroad.Zip}");

            // Work info
            AddTag("EMPLOYEE_WorkAddress", "Employee", companyName, "Адреса роботи", data.WorkAddressTag);
            AddTag("EMPLOYEE_Position", "Employee", companyName, "Позиція", data.PositionTag);
            AddTag("EMPLOYEE_PositionNumber", "Employee", companyName, "Номер позиції", data.PositionNumber);
            AddTag("EMPLOYEE_SalaryBrutto", "Employee", companyName, "Місячна зарплата (брутто)", data.MonthlySalaryBrutto.ToString());
            AddTag("EMPLOYEE_HourlySalary", "Employee", companyName, "Годинна зарплата", data.HourlySalary.ToString());
            AddTag("EMPLOYEE_ContractType", "Employee", companyName, "Тип договору", data.ContractType);
            AddTag("EMPLOYEE_Department", "Employee", companyName, "Відділ", data.Department);

            // Contact
            AddTag("EMPLOYEE_Phone", "Employee", companyName, "Телефон", data.Phone);
            AddTag("EMPLOYEE_Email", "Employee", companyName, "Email", data.Email);
            AddTag("EMPLOYEE_Status", "Employee", companyName, "Статус", data.Status);

            // Dates
            AddTag("EMPLOYEE_StartDate", "Employee", companyName, "Дата наступу на роботу", data.StartDate);
            AddTag("EMPLOYEE_ContractSignDate", "Employee", companyName, "Дата підписання договору", data.ContractSignDate);
        }

        /// <summary>
        /// Returns tag-value map combining company tags with specific employee data.
        /// </summary>
        public Dictionary<string, string> GetTagValueMapForEmployee(string companyName, EmployeeModels.EmployeeData employeeData)
        {
            var map = new Dictionary<string, string>();

            // Add company tags
            foreach (var tag in _tags.Where(t => t.CompanyName == companyName && t.Category != "Employee"))
            {
                map[tag.Tag] = tag.Value;
            }

            // Add employee-specific tags directly from data
            map["EMPLOYEE_FirstName"] = employeeData.FirstName;
            map["EMPLOYEE_LastName"] = employeeData.LastName;
            map["EMPLOYEE_FullName"] = $"{employeeData.FirstName} {employeeData.LastName}";
            map["EMPLOYEE_BirthDate"] = employeeData.BirthDate;
            map["EMPLOYEE_PassportNumber"] = employeeData.PassportNumber;
            map["EMPLOYEE_PassportCity"] = employeeData.PassportCity;
            map["EMPLOYEE_PassportCountry"] = employeeData.PassportCountry;
            map["EMPLOYEE_PassportExpiry"] = employeeData.PassportExpiry;
            map["EMPLOYEE_VisaNumber"] = employeeData.VisaNumber;
            map["EMPLOYEE_VisaType"] = employeeData.VisaType;
            map["EMPLOYEE_VisaExpiry"] = employeeData.VisaExpiry;
            map["EMPLOYEE_InsuranceCompany"] = employeeData.InsuranceCompanyShort;
            map["EMPLOYEE_InsuranceNumber"] = employeeData.InsuranceNumber;
            map["EMPLOYEE_InsuranceExpiry"] = employeeData.InsuranceExpiry;
            map["EMPLOYEE_LocalAddress_Street"] = employeeData.AddressLocal.Street;
            map["EMPLOYEE_LocalAddress_Number"] = employeeData.AddressLocal.Number;
            map["EMPLOYEE_LocalAddress_City"] = employeeData.AddressLocal.City;
            map["EMPLOYEE_LocalAddress_Zip"] = employeeData.AddressLocal.Zip;
            map["EMPLOYEE_LocalAddress_Full"] = $"{employeeData.AddressLocal.Street} {employeeData.AddressLocal.Number}, {employeeData.AddressLocal.City} {employeeData.AddressLocal.Zip}";
            map["EMPLOYEE_AbroadAddress_Street"] = employeeData.AddressAbroad.Street;
            map["EMPLOYEE_AbroadAddress_Number"] = employeeData.AddressAbroad.Number;
            map["EMPLOYEE_AbroadAddress_City"] = employeeData.AddressAbroad.City;
            map["EMPLOYEE_AbroadAddress_Zip"] = employeeData.AddressAbroad.Zip;
            map["EMPLOYEE_AbroadAddress_Full"] = $"{employeeData.AddressAbroad.Street} {employeeData.AddressAbroad.Number}, {employeeData.AddressAbroad.City} {employeeData.AddressAbroad.Zip}";
            map["EMPLOYEE_WorkAddress"] = employeeData.WorkAddressTag;
            map["EMPLOYEE_Position"] = employeeData.PositionTag;
            map["EMPLOYEE_PositionNumber"] = employeeData.PositionNumber;
            map["EMPLOYEE_SalaryBrutto"] = employeeData.MonthlySalaryBrutto.ToString();
            map["EMPLOYEE_HourlySalary"] = employeeData.HourlySalary.ToString();
            map["EMPLOYEE_ContractType"] = employeeData.ContractType;
            map["EMPLOYEE_Department"] = employeeData.Department;
            map["EMPLOYEE_Phone"] = employeeData.Phone;
            map["EMPLOYEE_Email"] = employeeData.Email;
            map["EMPLOYEE_Status"] = employeeData.Status;
            map["EMPLOYEE_StartDate"] = employeeData.StartDate;
            map["EMPLOYEE_ContractSignDate"] = employeeData.ContractSignDate;

            return map;
        }

        private void AddTag(string tag, string category, string companyName, string description, string value)
        {
            var existing = _tags.FirstOrDefault(t => t.Tag == tag && t.CompanyName == companyName);
            if (existing != null)
            {
                existing.Value = value;
                return;
            }

            _tags.Add(new TagEntry
            {
                Tag = tag,
                Category = category,
                CompanyName = companyName,
                Description = description,
                Value = value
            });
        }

        /// <summary>
        /// Removes all tags associated with a specific company name.
        /// </summary>
        public void RemoveTagsForCompany(string companyName)
        {
            if (string.IsNullOrEmpty(companyName)) return;
            _tags.RemoveAll(t => t.CompanyName == companyName);
        }

        public List<TagEntry> GetAllTags() => _tags;
        public List<TagEntry> GetTagsByCompany(string companyName) => _tags.Where(t => t.CompanyName == companyName).ToList();
        public List<TagEntry> GetTagsByCategory(string category) => _tags.Where(t => t.Category == category).ToList();

        /// <summary>
        /// Returns all tag definitions (without employee-specific values) for UI display.
        /// </summary>
        public List<TagEntry> GetAllTagDefinitions()
        {
            // Return unique tags by Tag name (company + employee placeholder tags)
            var companyTags = _tags.Where(t => t.Category != "Employee").ToList();

            // Add employee placeholder tag definitions with subcategories
            var employeeTags = new List<TagEntry>
            {
                // Особисті дані
                new() { Tag = "EMPLOYEE_FirstName", Category = "Employee.Personal", Description = "Ім'я працівника" },
                new() { Tag = "EMPLOYEE_LastName", Category = "Employee.Personal", Description = "Прізвище працівника" },
                new() { Tag = "EMPLOYEE_FullName", Category = "Employee.Personal", Description = "Повне ім'я працівника" },
                new() { Tag = "EMPLOYEE_BirthDate", Category = "Employee.Personal", Description = "Дата народження" },
                new() { Tag = "EMPLOYEE_Phone", Category = "Employee.Personal", Description = "Телефон" },
                new() { Tag = "EMPLOYEE_Email", Category = "Employee.Personal", Description = "Email" },
                new() { Tag = "EMPLOYEE_Status", Category = "Employee.Personal", Description = "Статус" },

                // Паспорт
                new() { Tag = "EMPLOYEE_PassportNumber", Category = "Employee.Passport", Description = "Номер паспорту" },
                new() { Tag = "EMPLOYEE_PassportCity", Category = "Employee.Passport", Description = "Місто народження" },
                new() { Tag = "EMPLOYEE_PassportCountry", Category = "Employee.Passport", Description = "Країна народження" },
                new() { Tag = "EMPLOYEE_PassportExpiry", Category = "Employee.Passport", Description = "Дата закінчення паспорту" },

                // Віза
                new() { Tag = "EMPLOYEE_VisaNumber", Category = "Employee.Visa", Description = "Номер візи" },
                new() { Tag = "EMPLOYEE_VisaType", Category = "Employee.Visa", Description = "Тип візи" },
                new() { Tag = "EMPLOYEE_VisaExpiry", Category = "Employee.Visa", Description = "Дата закінчення візи" },

                // Страховка
                new() { Tag = "EMPLOYEE_InsuranceCompany", Category = "Employee.Insurance", Description = "Страхова компанія" },
                new() { Tag = "EMPLOYEE_InsuranceNumber", Category = "Employee.Insurance", Description = "Номер страховки" },
                new() { Tag = "EMPLOYEE_InsuranceExpiry", Category = "Employee.Insurance", Description = "Дата закінчення страховки" },

                // Адреса проживання
                new() { Tag = "EMPLOYEE_LocalAddress_Street", Category = "Employee.LocalAddress", Description = "Вулиця" },
                new() { Tag = "EMPLOYEE_LocalAddress_Number", Category = "Employee.LocalAddress", Description = "Номер" },
                new() { Tag = "EMPLOYEE_LocalAddress_City", Category = "Employee.LocalAddress", Description = "Місто" },
                new() { Tag = "EMPLOYEE_LocalAddress_Zip", Category = "Employee.LocalAddress", Description = "Індекс" },
                new() { Tag = "EMPLOYEE_LocalAddress_Full", Category = "Employee.LocalAddress", Description = "Повна адреса" },

                // Адреса за кордоном
                new() { Tag = "EMPLOYEE_AbroadAddress_Street", Category = "Employee.AbroadAddress", Description = "Вулиця" },
                new() { Tag = "EMPLOYEE_AbroadAddress_Number", Category = "Employee.AbroadAddress", Description = "Номер" },
                new() { Tag = "EMPLOYEE_AbroadAddress_City", Category = "Employee.AbroadAddress", Description = "Місто" },
                new() { Tag = "EMPLOYEE_AbroadAddress_Zip", Category = "Employee.AbroadAddress", Description = "Індекс" },
                new() { Tag = "EMPLOYEE_AbroadAddress_Full", Category = "Employee.AbroadAddress", Description = "Повна адреса" },

                // Робота
                new() { Tag = "EMPLOYEE_WorkAddress", Category = "Employee.Work", Description = "Адреса роботи" },
                new() { Tag = "EMPLOYEE_Position", Category = "Employee.Work", Description = "Позиція" },
                new() { Tag = "EMPLOYEE_PositionNumber", Category = "Employee.Work", Description = "Номер позиції" },
                new() { Tag = "EMPLOYEE_SalaryBrutto", Category = "Employee.Work", Description = "Місячна зарплата (брутто)" },
                new() { Tag = "EMPLOYEE_HourlySalary", Category = "Employee.Work", Description = "Годинна зарплата" },
                new() { Tag = "EMPLOYEE_ContractType", Category = "Employee.Work", Description = "Тип договору" },
                new() { Tag = "EMPLOYEE_Department", Category = "Employee.Work", Description = "Відділ" },
                new() { Tag = "EMPLOYEE_StartDate", Category = "Employee.Work", Description = "Дата наступу на роботу" },
                new() { Tag = "EMPLOYEE_ContractSignDate", Category = "Employee.Work", Description = "Дата підписання договору" },
            };

            // Add agency placeholder tag definitions
            var agencyTags = new List<TagEntry>
            {
                new() { Tag = "AGENCY_Name", Category = "Agency", Description = "Назва агентства" },
                new() { Tag = "AGENCY_ICO", Category = "Agency", Description = "ІЧО агентства" },
                new() { Tag = "AGENCY_FullAddress", Category = "Agency", Description = "Повна адреса агентства" },
            };

            var result = new List<TagEntry>();
            result.AddRange(companyTags.Where(t => t.Category != "Agency")); // exclude duplicates
            result.AddRange(agencyTags);
            result.AddRange(employeeTags);
            return result;
        }

        public Dictionary<string, string> GetTagValueMap(string companyName)
        {
            return _tags
                .Where(t => t.CompanyName == companyName)
                .GroupBy(t => t.Tag)
                .ToDictionary(g => g.Key, g => g.First().Value);
        }
    }
}
