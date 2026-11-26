using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class VacancyTests
    {
        private static readonly DateTime Created = new(2026, 11, 10);

        private static VacancyEmployee Man(string id, int day) => new(id, "Man " + id, "male", new DateTime(2026, 11, day));
        private static VacancyEmployee Woman(string id, int day) => new(id, "Woman " + id, "female", new DateTime(2026, 11, day));

        [Fact]
        public void NewEmployeesAfterCreation_AreCountedByGender()
        {
            var vacancy = new Vacancy { Id = "a", FirmName = "DCK", Men = 2, Women = 1, CreatedAt = Created };
            var employees = new[] { Man("1", 5), Man("2", 10), Man("3", 12), Man("4", 13), Woman("5", 11) };

            var progress = VacancyMatcher.Compute(new[] { vacancy }, employees)["a"];

            Assert.Equal(3, progress.FoundCount);
            Assert.Equal(2, progress.MenFound);
            Assert.Equal(1, progress.WomenFound);
            Assert.DoesNotContain(progress.Found, p => p.EmployeeId == "1"); // started before the position existed
            Assert.DoesNotContain(progress.Found, p => p.EmployeeId == "4"); // no free place for a man any more
        }

        [Fact]
        public void TwoPositionsOfOneFirm_OldestFilledFirst_ExcludedAndClosedRespected()
        {
            var older = new Vacancy { Id = "old", Men = 1, CreatedAt = Created };
            var newer = new Vacancy { Id = "new", Men = 2, CreatedAt = Created.AddDays(1), ExcludedEmployeeIds = new List<string> { "3" } };
            var closed = new Vacancy { Id = "closed", Anyone = 5, CreatedAt = Created.AddDays(-5), ClosedAt = Created.AddDays(-1) };
            var employees = new[] { Man("1", 11), Man("2", 12), Man("3", 13), Man("4", 14) };

            var result = VacancyMatcher.Compute(new[] { newer, older, closed }, employees);

            Assert.Equal(new[] { "1" }, result["old"].Found.Select(p => p.EmployeeId));
            Assert.Equal(new[] { "2", "4" }, result["new"].Found.Select(p => p.EmployeeId));
            Assert.Empty(result["closed"].Found);
        }

        [Fact]
        public void ManualPeopleAndCouples()
        {
            var vacancy = new Vacancy
            {
                Id = "c", Couples = 1, Anyone = 1, CreatedAt = Created,
                ManualFound = new List<VacancyManualPerson>
                {
                    new() { Id = "m1", Name = "Olena", Gender = "female" },
                    new() { Id = "m2", EmployeeId = "9", Name = "old name" }
                }
            };
            var employees = new[] { Man("9", 1), Man("7", 20), Man("8", 21) };

            var progress = VacancyMatcher.Compute(new[] { vacancy }, employees)["c"];

            Assert.Equal(3, progress.FoundCount);
            Assert.Equal(1, progress.CouplesWomenFound);
            Assert.Equal(1, progress.CouplesMenFound);
            Assert.Equal("Man 9", progress.Found.Single(p => p.ManualId == "m2").Name);
            Assert.Equal(1, progress.AnyoneFound);
            Assert.Equal(3, vacancy.TotalNeeded); // one couple = 2 people + 1 anyone
        }

        [Fact]
        public void Service_SavesEachPositionAsOwnFile_DeleteMovesToDeleted()
        {
            var root = Path.Combine(Path.GetTempPath(), "vac-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var service = new VacancyService(() => root);
                var a = new Vacancy { FirmName = "DCK", Men = 2 };
                var b = new Vacancy { FirmName = "AGRO", Women = 1 };
                service.Save(a);
                service.Save(b);
                service.SaveFirmNote("DCK s.r.o. / Praha", "contact: Jan");
                service.SavePageSettings(new VacancyPageSettings { ContactPhone = "+420 1" });

                Assert.Equal(2, service.GetAll().Count);
                Assert.Equal("contact: Jan", service.GetFirmNote("DCK s.r.o. / Praha"));
                Assert.Equal("+420 1", service.GetPageSettings().ContactPhone);

                service.Delete(a.Id);
                Assert.Equal(new[] { b.Id }, service.GetAll().Select(v => v.Id));
                Assert.Single(Directory.GetFiles(Path.Combine(service.Folder, "_deleted")));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Pdf_PosterAndList_AreCreated()
        {
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            var folder = Path.Combine(Path.GetTempPath(), "vac-pdf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var item = new VacancyPdfItem
                {
                    Position = "Склад", City = "Březnice", Pay = "145 Kč/год", NeededFrom = "01.12.2026",
                    Description = "Комплектація", Conditions = new[] { "житло", "транспорт" }, Men = 4, Women = 2, Couples = 1
                };
                var contact = new VacancyPageSettings { ContactPhone = "+420 777 000 000", AgencyName = "Agency" };
                var poster = Path.Combine(folder, "poster.pdf");
                var list = Path.Combine(folder, "list.pdf");

                VacancyPdfExportService.GeneratePoster(poster, item, contact, new VacancyPdfLabels());
                VacancyPdfExportService.GenerateList(list, new[] { item, item }, contact, new VacancyPdfLabels(), "листопад 2026");

                Assert.True(new FileInfo(poster).Length > 1000);
                Assert.True(new FileInfo(list).Length > 1000);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
        [Fact]
        public void Editor_OffersFirmPositions_AndFillsEmptyPay()
        {
            var positions = new Dictionary<string, IReadOnlyList<Win11DesktopApp.ViewModels.VacancyEditorViewModel.FirmPosition>>
            {
                ["DCK"] = new[] { new Win11DesktopApp.ViewModels.VacancyEditorViewModel.FirmPosition("Skladník", 145m) },
                ["AGRO"] = new[]
                {
                    new Win11DesktopApp.ViewModels.VacancyEditorViewModel.FirmPosition("Pole", 0m),
                    new Win11DesktopApp.ViewModels.VacancyEditorViewModel.FirmPosition("Balení", 130m)
                }
            };
            var editor = new Win11DesktopApp.ViewModels.VacancyEditorViewModel(null, new[] { "AGRO", "DCK" }, positions);

            editor.FirmName = "AGRO";
            Assert.Equal(new[] { "Pole", "Balení" }, editor.PositionChoices);
            Assert.Equal(string.Empty, editor.Position ?? string.Empty);

            editor.FirmName = "DCK";
            Assert.Equal("Skladník", editor.Position); // the only position is picked by itself
            Assert.Contains("145", editor.Pay);
        }
        [Fact]
        public void AiRequest_HasNoFirmNameOrNotes()
        {
            var vacancy = new Vacancy
            {
                FirmName = "LESS & TIMBER, a.s.", Notes = "secret: pays late", Position = "Montáž", City = "Sušice",
                Men = 3, Pay = "200 Kč/год", Conditions = "житло безкоштовне", NeededFrom = "2026-12-01"
            };

            var request = VacancyAiWriter.BuildRequest(vacancy, "робота на монтажі", "+420 777", "Viber");

            Assert.DoesNotContain("TIMBER", request);
            Assert.DoesNotContain("secret", request);
            Assert.Contains("3 men", request);
            Assert.Contains("01.12.2026", request);
            Assert.Contains("робота на монтажі", request);
            Assert.Contains("+420 777 (Viber)", request);
        }

        [Fact]
        public void PdfAbout_DescriptionFirstThenConditions()
        {
            Assert.Equal("Пилорама.", VacancyPdfExportService.AboutText(new VacancyPdfItem { Description = " Пилорама. ", Conditions = new[] { "житло" } }));
            Assert.Equal("житло · 3 зміни", VacancyPdfExportService.AboutText(new VacancyPdfItem { Conditions = new[] { "житло", " ", "3 зміни" } }));
        }

        [Theory]
        [InlineData("{\"description\":\"Монтаж.\",\"ad\":\"🔨 Шукаємо\\n✔ житло\"}", "Монтаж.", "🔨 Шукаємо\n✔ житло")]
        [InlineData("```json\n{\"description\":\"A\",\"ad\":\"B\"}\n```", "A", "B")]
        [InlineData("Just a plain post", "", "Just a plain post")]
        public void AiAnswer_IsParsed(string answer, string description, string ad)
        {
            var result = VacancyAiWriter.ParseAnswer(answer);
            Assert.Equal(description, result.Description);
            Assert.Equal(ad, result.Ad);
        }
    }
}
