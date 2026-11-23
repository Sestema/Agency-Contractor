using System;
using System.Linq;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.ViewModels;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class DashboardMovementTests
    {
        [Theory]
        [InlineData("2026-11-01", "2026-11-12", "12 дн.")]
        [InlineData("2026-07-01", "2026-11-02", "4 міс.")]
        [InlineData("2025-09-15", "2026-11-20", "1 р. 2 міс.")]
        [InlineData("2024-11-02", "2026-11-02", "2 р.")]
        public void FormatWorkedDuration_UsesBiggestUnits(string start, string end, string expected)
        {
            Assert.Equal(expected, DashboardViewModel.FormatWorkedDuration(DateTime.Parse(start), DateTime.Parse(end), "р.", "міс.", "дн."));
        }

        [Fact]
        public void BuildTopFirmText_PicksFirmWithMostPeople()
        {
            var items = new[]
            {
                new MonthlyMovementItem { FirmName = "RUMPOLD-P" },
                new MonthlyMovementItem { FirmName = "DCK Holoubkov" },
                new MonthlyMovementItem { FirmName = "dck holoubkov" },
            };

            Assert.Equal("DCK Holoubkov +2", DashboardViewModel.BuildTopFirmText(items, "+"));
            Assert.Equal(string.Empty, DashboardViewModel.BuildTopFirmText(Array.Empty<MonthlyMovementItem>(), "+"));
        }

        [Fact]
        public void AreEmployeeDocumentsOk_FollowsTypeRules()
        {
            var far = DateTime.Today.AddYears(1).ToString("dd.MM.yyyy");
            var soon = DateTime.Today.AddDays(10).ToString("dd.MM.yyyy");
            var past = DateTime.Today.AddDays(-3).ToString("dd.MM.yyyy");

            Assert.True(DashboardViewModel.AreEmployeeDocumentsOk(new EmployeeSummary { PassportExpiry = far, VisaExpiry = far, InsuranceExpiry = far }));
            Assert.False(DashboardViewModel.AreEmployeeDocumentsOk(new EmployeeSummary { PassportExpiry = far, VisaExpiry = far, InsuranceExpiry = soon }));
            Assert.False(DashboardViewModel.AreEmployeeDocumentsOk(new EmployeeSummary { PassportExpiry = past, InsuranceExpiry = far }));
            // EU citizens have no visa, people without a permit type ignore the permit date.
            Assert.True(DashboardViewModel.AreEmployeeDocumentsOk(new EmployeeSummary { EmployeeType = "eu_citizen", PassportExpiry = far, VisaExpiry = past, InsuranceExpiry = far }));
            Assert.True(DashboardViewModel.AreEmployeeDocumentsOk(new EmployeeSummary { PassportExpiry = far, InsuranceExpiry = far, WorkPermitExpiry = past }));
            Assert.False(DashboardViewModel.AreEmployeeDocumentsOk(new EmployeeSummary { EmployeeType = "work_permit", PassportExpiry = far, VisaExpiry = far, InsuranceExpiry = far, WorkPermitExpiry = past }));
        }

        [Fact]
        public void BuildDocumentStatusPerson_SortsProblemsAndPicksStatus()
        {
            var far = DateTime.Today.AddYears(1).ToString("dd.MM.yyyy");
            var soon = DateTime.Today.AddDays(10).ToString("dd.MM.yyyy");
            var past = DateTime.Today.AddDays(-3).ToString("dd.MM.yyyy");

            var expired = DashboardViewModel.BuildDocumentStatusPerson(
                new EmployeeSummary { FullName = "Anita Pál", PassportExpiry = far, VisaExpiry = soon, InsuranceExpiry = past }, "DCK");
            Assert.Equal("expired", expired.Status);
            Assert.Equal(-3, expired.WorstDays);
            Assert.Equal(new[] { "expired", "soon" }, expired.Chips.Select(chip => chip.Kind));
            Assert.Equal("AP", expired.Initials);

            var ok = DashboardViewModel.BuildDocumentStatusPerson(
                new EmployeeSummary { EmployeeType = "eu_citizen", PassportExpiry = far, VisaExpiry = past, InsuranceExpiry = far }, "DCK");
            Assert.Equal("ok", ok.Status);
            Assert.Single(ok.Chips);
            Assert.Equal("next", ok.Chips[0].Kind);
        }

        [Fact]
        public void BuildTopProblemFirmText_CountsOnlyPeopleWithProblems()
        {
            var people = new[]
            {
                new DocumentStatusPerson { FirmName = "Bidfood", Status = "expired" },
                new DocumentStatusPerson { FirmName = "bidfood", Status = "soon" },
                new DocumentStatusPerson { FirmName = "DCK", Status = "ok" },
                new DocumentStatusPerson { FirmName = "DCK", Status = "ok" },
                new DocumentStatusPerson { FirmName = "DCK", Status = "ok" },
            };

            Assert.Equal("Bidfood · 2", DashboardViewModel.BuildTopProblemFirmText(people));
            Assert.Equal(string.Empty, DashboardViewModel.BuildTopProblemFirmText(people.Where(person => person.Status == "ok")));
        }

        [Theory]
        [InlineData("Anita Pál", "AP")]
        [InlineData("olena  maria hamor", "OH")]
        [InlineData("Ivan", "I")]
        [InlineData("", "")]
        public void BuildInitials_FirstAndLastWord(string name, string expected)
        {
            Assert.Equal(expected, DashboardViewModel.BuildInitials(name));
        }
    }
}
