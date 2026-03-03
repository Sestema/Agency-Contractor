using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Win11DesktopApp.Converters;
using Win11DesktopApp.Models;
using EmployeeModels = Win11DesktopApp.EmployeeModels;

namespace Win11DesktopApp.Services
{
    public class TagCatalogService
    {
        private List<TagEntry> _tags = new List<TagEntry>();

        private static string Res(string key)
        {
            try { return Application.Current.FindResource(key) as string ?? key; }
            catch { return key; }
        }

        private static string ResF(string key, params object[] args)
        {
            var fmt = Res(key);
            try { return string.Format(fmt, args); }
            catch { return fmt; }
        }

        public void AddTagsForCompany(EmployerCompany employer, AgencyCompany agency)
        {
            AddTagsForEmployerOnly(employer);

            AddTag("AGENCY_Name", "Agency", employer.Name, Res("TagDescAgencyName"), agency.Name);
            AddTag("AGENCY_ICO", "Agency", employer.Name, Res("TagDescAgencyICO"), agency.ICO);
            AddTag("AGENCY_FullAddress", "Agency", employer.Name, Res("TagDescAgencyAddress"), agency.FullAddress);
        }

        public void AddTagsForEmployerOnly(EmployerCompany employer)
        {
            AddTag("COMPANY_Name", "Company", employer.Name, Res("TagDescCompanyName"), employer.Name);
            AddTag("COMPANY_ICO", "Company", employer.Name, Res("TagDescCompanyICO"), employer.ICO);
            AddTag("COMPANY_WeeklyWorkHours", "Company", employer.Name, Res("TagDescCompanyWeeklyHours"), employer.WeeklyWorkHours.ToString());
            AddTag("COMPANY_DailyWorkHours", "Company", employer.Name, Res("TagDescCompanyDailyHours"), employer.DailyWorkHours.ToString());
            AddTag("COMPANY_ShiftCount", "Company", employer.Name, Res("TagDescCompanyShiftCount"), employer.ShiftCount.ToString());

            for (int i = 0; i < employer.Addresses.Count; i++)
            {
                var addr = employer.Addresses[i];
                var idx = i + 1;
                AddTag($"COMPANY_ADDR{idx}_Street", "Company", employer.Name, ResF("TagDescCompanyAddrStreet", idx), addr.Street);
                AddTag($"COMPANY_ADDR{idx}_Number", "Company", employer.Name, ResF("TagDescCompanyAddrNumber", idx), addr.Number);
                AddTag($"COMPANY_ADDR{idx}_City", "Company", employer.Name, ResF("TagDescCompanyAddrCity", idx), addr.City);
                AddTag($"COMPANY_ADDR{idx}_Zip", "Company", employer.Name, ResF("TagDescCompanyAddrZip", idx), addr.ZipCode);
                AddTag($"COMPANY_ADDR{idx}_Full", "Company", employer.Name, ResF("TagDescCompanyAddrFull", idx),
                    $"{addr.Street} {addr.Number}, {addr.City} {addr.ZipCode}");
            }

            for (int i = 0; i < employer.Positions.Count; i++)
            {
                var pos = employer.Positions[i];
                var idx = i + 1;
                AddTag($"COMPANY_POS{idx}_Title", "Company", employer.Name, ResF("TagDescCompanyPosTitle", idx), pos.Title);
                AddTag($"COMPANY_POS{idx}_Number", "Company", employer.Name, ResF("TagDescCompanyPosNumber", idx), pos.PositionNumber);
                AddTag($"COMPANY_POS{idx}_SalaryBrutto", "Company", employer.Name, ResF("TagDescCompanyPosSalary", idx), pos.MonthlySalaryBrutto.ToString());
                AddTag($"COMPANY_POS{idx}_HourlySalary", "Company", employer.Name, ResF("TagDescCompanyPosHourly", idx), pos.HourlySalary.ToString());
            }
        }

        public void AddTagsForEmployee(string companyName, EmployeeModels.EmployeeData data)
        {
            AddTag("EMPLOYEE_FirstName", "Employee", companyName, Res("TagDescEmpFirstName"), data.FirstName);
            AddTag("EMPLOYEE_LastName", "Employee", companyName, Res("TagDescEmpLastName"), data.LastName);
            AddTag("EMPLOYEE_FullName", "Employee", companyName, Res("TagDescEmpFullName"), $"{data.FirstName} {data.LastName}");
            AddTag("EMPLOYEE_BirthDate", "Employee", companyName, Res("TagDescEmpBirthDate"), data.BirthDate);

            AddTag("EMPLOYEE_PassportNumber", "Employee", companyName, Res("TagDescEmpPassportNumber"), data.PassportNumber);
            AddTag("EMPLOYEE_PassportCity", "Employee", companyName, Res("TagDescEmpPassportCity"), data.PassportCity);
            AddTag("EMPLOYEE_PassportCountry", "Employee", companyName, Res("TagDescEmpPassportCountry"), data.PassportCountry);
            AddTag("EMPLOYEE_PassportExpiry", "Employee", companyName, Res("TagDescEmpPassportExpiry"), data.PassportExpiry);

            AddTag("EMPLOYEE_VisaNumber", "Employee", companyName, Res("TagDescEmpVisaNumber"), data.VisaNumber);
            AddTag("EMPLOYEE_VisaType", "Employee", companyName, Res("TagDescEmpVisaType"), data.VisaType);
            AddTag("EMPLOYEE_VisaExpiry", "Employee", companyName, Res("TagDescEmpVisaExpiry"), data.VisaExpiry);

            AddTag("EMPLOYEE_InsuranceCompany", "Employee", companyName, Res("TagDescEmpInsCompany"), data.InsuranceCompanyShort);
            AddTag("EMPLOYEE_InsuranceNumber", "Employee", companyName, Res("TagDescEmpInsNumber"), data.InsuranceNumber);
            AddTag("EMPLOYEE_InsuranceExpiry", "Employee", companyName, Res("TagDescEmpInsExpiry"), data.InsuranceExpiry);

            AddTag("EMPLOYEE_WorkPermitName", "Employee", companyName, Res("TagDescEmpWpName"), data.WorkPermitName);
            AddTag("EMPLOYEE_WorkPermitNumber", "Employee", companyName, Res("TagDescEmpWpNumber"), data.WorkPermitNumber);
            AddTag("EMPLOYEE_WorkPermitType", "Employee", companyName, Res("TagDescEmpWpType"), data.WorkPermitType);
            AddTag("EMPLOYEE_WorkPermitIssueDate", "Employee", companyName, Res("TagDescEmpWpIssueDate"), data.WorkPermitIssueDate);
            AddTag("EMPLOYEE_WorkPermitExpiry", "Employee", companyName, Res("TagDescEmpWpExpiry"), data.WorkPermitExpiry);
            AddTag("EMPLOYEE_WorkPermitAuthority", "Employee", companyName, Res("TagDescEmpWpAuthority"), data.WorkPermitAuthority);
            AddTag("EMPLOYEE_EmployeeType", "Employee", companyName, Res("TagDescEmpType"), data.EmployeeType);

            var localAddr = data.AddressLocal ?? new EmployeeModels.EmployeeAddress();
            AddTag("EMPLOYEE_LocalAddress_Street", "Employee", companyName, Res("TagDescEmpLocalStreet"), localAddr.Street);
            AddTag("EMPLOYEE_LocalAddress_Number", "Employee", companyName, Res("TagDescEmpLocalNumber"), localAddr.Number);
            AddTag("EMPLOYEE_LocalAddress_City", "Employee", companyName, Res("TagDescEmpLocalCity"), localAddr.City);
            AddTag("EMPLOYEE_LocalAddress_Zip", "Employee", companyName, Res("TagDescEmpLocalZip"), localAddr.Zip);
            AddTag("EMPLOYEE_LocalAddress_Full", "Employee", companyName, Res("TagDescEmpLocalFull"),
                $"{localAddr.Street} {localAddr.Number}, {localAddr.City} {localAddr.Zip}");

            var abroadAddr = data.AddressAbroad ?? new EmployeeModels.EmployeeAddress();
            AddTag("EMPLOYEE_AbroadAddress_Street", "Employee", companyName, Res("TagDescEmpAbroadStreet"), abroadAddr.Street);
            AddTag("EMPLOYEE_AbroadAddress_Number", "Employee", companyName, Res("TagDescEmpAbroadNumber"), abroadAddr.Number);
            AddTag("EMPLOYEE_AbroadAddress_City", "Employee", companyName, Res("TagDescEmpAbroadCity"), abroadAddr.City);
            AddTag("EMPLOYEE_AbroadAddress_Zip", "Employee", companyName, Res("TagDescEmpAbroadZip"), abroadAddr.Zip);
            AddTag("EMPLOYEE_AbroadAddress_Full", "Employee", companyName, Res("TagDescEmpAbroadFull"),
                $"{abroadAddr.Street} {abroadAddr.Number}, {abroadAddr.City} {abroadAddr.Zip}");

            AddTag("EMPLOYEE_WorkAddress", "Employee", companyName, Res("TagDescEmpWorkAddress"), data.WorkAddressTag);
            AddTag("EMPLOYEE_Position", "Employee", companyName, Res("TagDescEmpPosition"), data.PositionTag);
            AddTag("EMPLOYEE_PositionNumber", "Employee", companyName, Res("TagDescEmpPosNumber"), data.PositionNumber);
            AddTag("EMPLOYEE_SalaryBrutto", "Employee", companyName, Res("TagDescEmpSalary"), data.MonthlySalaryBrutto.ToString());
            AddTag("EMPLOYEE_HourlySalary", "Employee", companyName, Res("TagDescEmpHourly"), data.HourlySalary.ToString());
            AddTag("EMPLOYEE_ContractType", "Employee", companyName, Res("TagDescEmpContractType"), data.ContractType);
            AddTag("EMPLOYEE_Department", "Employee", companyName, Res("TagDescEmpDepartment"), data.Department);

            AddTag("EMPLOYEE_Phone", "Employee", companyName, Res("TagDescEmpPhone"), data.Phone);
            AddTag("EMPLOYEE_Email", "Employee", companyName, Res("TagDescEmpEmail"), data.Email);
            AddTag("EMPLOYEE_Status", "Employee", companyName, Res("TagDescEmpStatus"), StatusHelper.GetDisplayText(data.Status));

            AddTag("EMPLOYEE_StartDate", "Employee", companyName, Res("TagDescEmpStartDate"), data.StartDate);
            AddTag("EMPLOYEE_ContractSignDate", "Employee", companyName, Res("TagDescEmpSignDate"), data.ContractSignDate);
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
            map["EMPLOYEE_WorkPermitName"] = employeeData.WorkPermitName;
            map["EMPLOYEE_WorkPermitNumber"] = employeeData.WorkPermitNumber;
            map["EMPLOYEE_WorkPermitType"] = employeeData.WorkPermitType;
            map["EMPLOYEE_WorkPermitIssueDate"] = employeeData.WorkPermitIssueDate;
            map["EMPLOYEE_WorkPermitExpiry"] = employeeData.WorkPermitExpiry;
            map["EMPLOYEE_WorkPermitAuthority"] = employeeData.WorkPermitAuthority;
            map["EMPLOYEE_EmployeeType"] = employeeData.EmployeeType;
            var local = employeeData.AddressLocal ?? new EmployeeModels.EmployeeAddress();
            map["EMPLOYEE_LocalAddress_Street"] = local.Street;
            map["EMPLOYEE_LocalAddress_Number"] = local.Number;
            map["EMPLOYEE_LocalAddress_City"] = local.City;
            map["EMPLOYEE_LocalAddress_Zip"] = local.Zip;
            map["EMPLOYEE_LocalAddress_Full"] = $"{local.Street} {local.Number}, {local.City} {local.Zip}";
            var abroad = employeeData.AddressAbroad ?? new EmployeeModels.EmployeeAddress();
            map["EMPLOYEE_AbroadAddress_Street"] = abroad.Street;
            map["EMPLOYEE_AbroadAddress_Number"] = abroad.Number;
            map["EMPLOYEE_AbroadAddress_City"] = abroad.City;
            map["EMPLOYEE_AbroadAddress_Zip"] = abroad.Zip;
            map["EMPLOYEE_AbroadAddress_Full"] = $"{abroad.Street} {abroad.Number}, {abroad.City} {abroad.Zip}";
            map["EMPLOYEE_WorkAddress"] = employeeData.WorkAddressTag;
            map["EMPLOYEE_Position"] = employeeData.PositionTag;
            map["EMPLOYEE_PositionNumber"] = employeeData.PositionNumber;
            map["EMPLOYEE_SalaryBrutto"] = employeeData.MonthlySalaryBrutto.ToString();
            map["EMPLOYEE_HourlySalary"] = employeeData.HourlySalary.ToString();
            map["EMPLOYEE_ContractType"] = employeeData.ContractType;
            map["EMPLOYEE_Department"] = employeeData.Department;
            map["EMPLOYEE_Phone"] = employeeData.Phone;
            map["EMPLOYEE_Email"] = employeeData.Email;
            map["EMPLOYEE_Status"] = StatusHelper.GetDisplayText(employeeData.Status);
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

        private static readonly Dictionary<string, string> CompanyTagResKeys = new()
        {
            { "COMPANY_Name", "TagDescCompanyName" },
            { "COMPANY_ICO", "TagDescCompanyICO" },
            { "COMPANY_WeeklyWorkHours", "TagDescCompanyWeeklyHours" },
            { "COMPANY_DailyWorkHours", "TagDescCompanyDailyHours" },
            { "COMPANY_ShiftCount", "TagDescCompanyShiftCount" },
        };

        private static string ResolveCompanyTagDescription(string tagName)
        {
            if (CompanyTagResKeys.TryGetValue(tagName, out var resKey))
                return Res(resKey);

            if (tagName.StartsWith("COMPANY_ADDR"))
            {
                var rest = tagName.Substring("COMPANY_ADDR".Length);
                var sep = rest.IndexOf('_');
                if (sep > 0)
                {
                    var idx = rest.Substring(0, sep);
                    var field = rest.Substring(sep + 1);
                    return field switch
                    {
                        "Street" => ResF("TagDescCompanyAddrStreet", idx),
                        "Number" => ResF("TagDescCompanyAddrNumber", idx),
                        "City" => ResF("TagDescCompanyAddrCity", idx),
                        "Zip" => ResF("TagDescCompanyAddrZip", idx),
                        "Full" => ResF("TagDescCompanyAddrFull", idx),
                        _ => tagName
                    };
                }
            }

            if (tagName.StartsWith("COMPANY_POS"))
            {
                var rest = tagName.Substring("COMPANY_POS".Length);
                var sep = rest.IndexOf('_');
                if (sep > 0)
                {
                    var idx = rest.Substring(0, sep);
                    var field = rest.Substring(sep + 1);
                    return field switch
                    {
                        "Title" => ResF("TagDescCompanyPosTitle", idx),
                        "Number" => ResF("TagDescCompanyPosNumber", idx),
                        "SalaryBrutto" => ResF("TagDescCompanyPosSalary", idx),
                        "HourlySalary" => ResF("TagDescCompanyPosHourly", idx),
                        _ => tagName
                    };
                }
            }

            return tagName;
        }

        /// <summary>
        /// Returns all tag definitions (without employee-specific values) for UI display.
        /// </summary>
        public List<TagEntry> GetAllTagDefinitions()
        {
            var companyTags = _tags
                .Where(t => t.Category != "Employee")
                .GroupBy(t => t.Tag)
                .Select(g => g.First())
                .ToList();
            foreach (var tag in companyTags)
                tag.Description = ResolveCompanyTagDescription(tag.Tag);

            var employeeTags = new List<TagEntry>
            {
                new() { Tag = "EMPLOYEE_FirstName", Category = "Employee.Personal", Description = Res("TagDescEmpFirstName") },
                new() { Tag = "EMPLOYEE_LastName", Category = "Employee.Personal", Description = Res("TagDescEmpLastName") },
                new() { Tag = "EMPLOYEE_FullName", Category = "Employee.Personal", Description = Res("TagDescEmpFullName") },
                new() { Tag = "EMPLOYEE_BirthDate", Category = "Employee.Personal", Description = Res("TagDescEmpBirthDate") },
                new() { Tag = "EMPLOYEE_Phone", Category = "Employee.Personal", Description = Res("TagDescEmpPhone") },
                new() { Tag = "EMPLOYEE_Email", Category = "Employee.Personal", Description = Res("TagDescEmpEmail") },
                new() { Tag = "EMPLOYEE_Status", Category = "Employee.Personal", Description = Res("TagDescEmpStatus") },

                new() { Tag = "EMPLOYEE_PassportNumber", Category = "Employee.Passport", Description = Res("TagDescEmpPassportNumber") },
                new() { Tag = "EMPLOYEE_PassportCity", Category = "Employee.Passport", Description = Res("TagDescEmpPassportCity") },
                new() { Tag = "EMPLOYEE_PassportCountry", Category = "Employee.Passport", Description = Res("TagDescEmpPassportCountry") },
                new() { Tag = "EMPLOYEE_PassportExpiry", Category = "Employee.Passport", Description = Res("TagDescEmpPassportExpiry") },

                new() { Tag = "EMPLOYEE_VisaNumber", Category = "Employee.Visa", Description = Res("TagDescEmpVisaNumber") },
                new() { Tag = "EMPLOYEE_VisaType", Category = "Employee.Visa", Description = Res("TagDescEmpVisaType") },
                new() { Tag = "EMPLOYEE_VisaExpiry", Category = "Employee.Visa", Description = Res("TagDescEmpVisaExpiry") },

                new() { Tag = "EMPLOYEE_InsuranceCompany", Category = "Employee.Insurance", Description = Res("TagDescEmpInsCompany") },
                new() { Tag = "EMPLOYEE_InsuranceNumber", Category = "Employee.Insurance", Description = Res("TagDescEmpInsNumber") },
                new() { Tag = "EMPLOYEE_InsuranceExpiry", Category = "Employee.Insurance", Description = Res("TagDescEmpInsExpiry") },

                new() { Tag = "EMPLOYEE_WorkPermitName", Category = "Employee.WorkPermit", Description = Res("TagDescEmpWpName") },
                new() { Tag = "EMPLOYEE_WorkPermitNumber", Category = "Employee.WorkPermit", Description = Res("TagDescEmpWpNumber") },
                new() { Tag = "EMPLOYEE_WorkPermitType", Category = "Employee.WorkPermit", Description = Res("TagDescEmpWpType") },
                new() { Tag = "EMPLOYEE_WorkPermitIssueDate", Category = "Employee.WorkPermit", Description = Res("TagDescEmpWpIssueDate") },
                new() { Tag = "EMPLOYEE_WorkPermitExpiry", Category = "Employee.WorkPermit", Description = Res("TagDescEmpWpExpiry") },
                new() { Tag = "EMPLOYEE_WorkPermitAuthority", Category = "Employee.WorkPermit", Description = Res("TagDescEmpWpAuthority") },
                new() { Tag = "EMPLOYEE_EmployeeType", Category = "Employee", Description = Res("TagDescEmpType") },

                new() { Tag = "EMPLOYEE_LocalAddress_Street", Category = "Employee.LocalAddress", Description = Res("TagDescEmpLocalStreet") },
                new() { Tag = "EMPLOYEE_LocalAddress_Number", Category = "Employee.LocalAddress", Description = Res("TagDescEmpLocalNumber") },
                new() { Tag = "EMPLOYEE_LocalAddress_City", Category = "Employee.LocalAddress", Description = Res("TagDescEmpLocalCity") },
                new() { Tag = "EMPLOYEE_LocalAddress_Zip", Category = "Employee.LocalAddress", Description = Res("TagDescEmpLocalZip") },
                new() { Tag = "EMPLOYEE_LocalAddress_Full", Category = "Employee.LocalAddress", Description = Res("TagDescEmpLocalFull") },

                new() { Tag = "EMPLOYEE_AbroadAddress_Street", Category = "Employee.AbroadAddress", Description = Res("TagDescEmpAbroadStreet") },
                new() { Tag = "EMPLOYEE_AbroadAddress_Number", Category = "Employee.AbroadAddress", Description = Res("TagDescEmpAbroadNumber") },
                new() { Tag = "EMPLOYEE_AbroadAddress_City", Category = "Employee.AbroadAddress", Description = Res("TagDescEmpAbroadCity") },
                new() { Tag = "EMPLOYEE_AbroadAddress_Zip", Category = "Employee.AbroadAddress", Description = Res("TagDescEmpAbroadZip") },
                new() { Tag = "EMPLOYEE_AbroadAddress_Full", Category = "Employee.AbroadAddress", Description = Res("TagDescEmpAbroadFull") },

                new() { Tag = "EMPLOYEE_WorkAddress", Category = "Employee.Work", Description = Res("TagDescEmpWorkAddress") },
                new() { Tag = "EMPLOYEE_Position", Category = "Employee.Work", Description = Res("TagDescEmpPosition") },
                new() { Tag = "EMPLOYEE_PositionNumber", Category = "Employee.Work", Description = Res("TagDescEmpPosNumber") },
                new() { Tag = "EMPLOYEE_SalaryBrutto", Category = "Employee.Work", Description = Res("TagDescEmpSalary") },
                new() { Tag = "EMPLOYEE_HourlySalary", Category = "Employee.Work", Description = Res("TagDescEmpHourly") },
                new() { Tag = "EMPLOYEE_ContractType", Category = "Employee.Work", Description = Res("TagDescEmpContractType") },
                new() { Tag = "EMPLOYEE_Department", Category = "Employee.Work", Description = Res("TagDescEmpDepartment") },
                new() { Tag = "EMPLOYEE_StartDate", Category = "Employee.Work", Description = Res("TagDescEmpStartDate") },
                new() { Tag = "EMPLOYEE_ContractSignDate", Category = "Employee.Work", Description = Res("TagDescEmpSignDate") },
            };

            var agencyTags = new List<TagEntry>
            {
                new() { Tag = "AGENCY_Name", Category = "Agency", Description = Res("TagDescAgencyName") },
                new() { Tag = "AGENCY_ICO", Category = "Agency", Description = Res("TagDescAgencyICO") },
                new() { Tag = "AGENCY_FullAddress", Category = "Agency", Description = Res("TagDescAgencyAddress") },
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
