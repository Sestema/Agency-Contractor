using System.Collections.Generic;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public sealed class AIScanPromptsTests
    {
        [Fact]
        public void ValidateAndCleanParsedFields_ShouldRemoveVisaStartDate_WhenItMatchesBirthDate()
        {
            var parsed = new Dictionary<string, string>
            {
                ["BirthDate"] = "04.03.1996",
                ["VisaStartDate"] = "04.03.1996",
                ["VisaExpiry"] = "04.03.2027"
            };

            var cleaned = AIScanPrompts.ValidateAndCleanParsedFields("visa", parsed);

            Assert.False(cleaned.ContainsKey("VisaStartDate"));
            Assert.Equal("04.03.2027", cleaned["VisaExpiry"]);
        }

        [Fact]
        public void ValidateAndCleanParsedFields_ShouldRemoveVisaStartDate_WhenItIsAfterVisaExpiry()
        {
            var parsed = new Dictionary<string, string>
            {
                ["VisaStartDate"] = "10.05.2027",
                ["VisaExpiry"] = "09.05.2027"
            };

            var cleaned = AIScanPrompts.ValidateAndCleanParsedFields("visa", parsed);

            Assert.False(cleaned.ContainsKey("VisaStartDate"));
            Assert.Equal("09.05.2027", cleaned["VisaExpiry"]);
        }

        [Fact]
        public void ValidateAndCleanParsedFields_ShouldRemovePassportAuthority_WhenItIsCountryOrPassportNumber()
        {
            var countryAuthority = AIScanPrompts.ValidateAndCleanParsedFields("passport", new Dictionary<string, string>
            {
                ["PassportAuthority"] = "Ukraine",
                ["PassportNumber"] = "GB780524"
            });
            var numberAuthority = AIScanPrompts.ValidateAndCleanParsedFields("passport", new Dictionary<string, string>
            {
                ["PassportAuthority"] = "GB780524",
                ["PassportNumber"] = "GB780524"
            });

            Assert.False(countryAuthority.ContainsKey("PassportAuthority"));
            Assert.False(numberAuthority.ContainsKey("PassportAuthority"));
        }

        [Fact]
        public void ValidateAndCleanParsedFields_ShouldKeepFourDigitPassportAuthorityCode()
        {
            var parsed = new Dictionary<string, string>
            {
                ["PassportAuthority"] = "5142"
            };

            var cleaned = AIScanPrompts.ValidateAndCleanParsedFields("passport", parsed);

            Assert.Equal("5142", cleaned["PassportAuthority"]);
        }

        [Fact]
        public void ValidateAndCleanParsedFields_ShouldKeepRealAuthorityText()
        {
            var parsed = new Dictionary<string, string>
            {
                ["PassportAuthority"] = "MV ČR OAMP"
            };

            var cleaned = AIScanPrompts.ValidateAndCleanParsedFields("id_card_back", parsed);

            Assert.Equal("MV ČR OAMP", cleaned["PassportAuthority"]);
        }

        [Fact]
        public void ValidateAndCleanParsedFields_ShouldNormalizeKnownCountryCodes()
        {
            var parsed = new Dictionary<string, string>
            {
                ["PassportCountry"] = "UKR",
                ["IssuingCountry"] = "CZE",
                ["Citizenship"] = "ROU"
            };

            var cleaned = AIScanPrompts.ValidateAndCleanParsedFields("passport", parsed);

            Assert.Equal("Ukraine", cleaned["PassportCountry"]);
            Assert.Equal("Czech Republic", cleaned["IssuingCountry"]);
            Assert.Equal("Romania", cleaned["Citizenship"]);
        }

        [Fact]
        public void ValidateAndCleanParsedFields_ShouldKeepPolishPeselAndNationality()
        {
            var parsed = new Dictionary<string, string>
            {
                ["RodneCislo"] = "81081509314",
                ["Citizenship"] = "POLSKIE",
                ["IssuingCountry"] = "Rzeczpospolita Polska",
                ["PassportCity"] = "Bielsko-Biała",
                ["PassportAuthority"] = "BURMISTRZ M. I G. TWARDOGÓRA"
            };

            var cleaned = AIScanPrompts.ValidateAndCleanParsedFields("id_card_back", parsed);

            Assert.Equal("81081509314", cleaned["RodneCislo"]);
            Assert.Equal("Poland", cleaned["Citizenship"]);
            Assert.Equal("Poland", cleaned["IssuingCountry"]);
            Assert.Equal("Bielsko-Biała", cleaned["PassportCity"]);
            Assert.Equal("BURMISTRZ M. I G. TWARDOGÓRA", cleaned["PassportAuthority"]);
        }

        [Fact]
        public void ValidateAndCleanParsedFields_ShouldKeepNineDigitCzechCitizenIdNumber()
        {
            var parsed = new Dictionary<string, string>
            {
                ["CardKind"] = "ceska_obcanka",
                ["WorkPermitName"] = "Ceska obcanka",
                ["PassportNumber"] = "218096814"
            };

            var cleaned = AIScanPrompts.ValidateAndCleanParsedFields("id_card", parsed);

            Assert.Equal("218096814", cleaned["PassportNumber"]);
        }

        [Fact]
        public void ValidateAndCleanParsedFields_ShouldKeepNineDigitCzechResidenceCardNumber()
        {
            var parsed = new Dictionary<string, string>
            {
                ["CardKind"] = "czech_residence",
                ["WorkPermitName"] = "Trvalý pobyt",
                ["PassportNumber"] = "001237510",
                ["VisaNumber"] = "001237510",
                ["Citizenship"] = "Ukraine",
                ["IssuingCountry"] = "Czech Republic",
                ["PassportAuthority"] = "MV ČR PLZEŇ"
            };

            var cleaned = AIScanPrompts.ValidateAndCleanParsedFields("id_card", parsed);

            Assert.Equal("001237510", cleaned["PassportNumber"]);
        }

        [Fact]
        public void ValidateAndCleanParsedFields_ShouldRemoveNineDigitNumberFromPassportScan()
        {
            var parsed = new Dictionary<string, string>
            {
                ["PassportNumber"] = "218096814"
            };

            var cleaned = AIScanPrompts.ValidateAndCleanParsedFields("passport", parsed);

            Assert.False(cleaned.ContainsKey("PassportNumber"));
        }

        [Fact]
        public void NormalizeScanGender_ShouldTreatPolishFemaleMarkAsFemale()
        {
            Assert.Equal("female", AIScanPrompts.NormalizeScanGender("K"));
            Assert.Equal("male", AIScanPrompts.NormalizeScanGender("M"));
        }
    }
}
