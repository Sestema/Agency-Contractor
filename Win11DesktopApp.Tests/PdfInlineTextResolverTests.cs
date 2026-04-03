using System.Collections.Generic;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Tests
{
    public class PdfInlineTextResolverTests
    {
        [Fact]
        public void ResolveTemplate_ReplacesMultipleTagsAndKeepsLiteralText()
        {
            var values = new Dictionary<string, string>
            {
                ["EMPLOYEE_BirthCountry"] = "Ukraine",
                ["EMPLOYEE_Citizenship"] = "Czech Republic"
            };

            var result = PdfInlineTextResolver.ResolveTemplate(
                "born in ${EMPLOYEE_BirthCountry}, citizen of ${EMPLOYEE_Citizenship}.",
                values);

            Assert.Equal("born in Ukraine, citizen of Czech Republic.", result);
        }

        [Fact]
        public void ResolveTemplate_UsesEmptyStringForUnknownTags()
        {
            var values = new Dictionary<string, string>
            {
                ["KNOWN_TAG"] = "value"
            };

            var result = PdfInlineTextResolver.ResolveTemplate(
                "A=${KNOWN_TAG}; B=${MISSING_TAG}",
                values);

            Assert.Equal("A=value; B=", result);
        }
    }
}
