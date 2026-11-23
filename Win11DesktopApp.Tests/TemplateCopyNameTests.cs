using System;
using System.Collections.Generic;
using Win11DesktopApp.ViewModels;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class TemplateCopyNameTests
    {
        private static HashSet<string> Names(params string[] names) => new(names, StringComparer.OrdinalIgnoreCase);

        [Fact]
        public void FreeName_StaysAsIs()
        {
            Assert.Equal("Pracovní smlouva", TemplatesViewModel.BuildUniqueTemplateName(" Pracovní smlouva ", Names("Pokyn")));
        }

        [Fact]
        public void TakenName_GetsNumber()
        {
            Assert.Equal("Pracovní smlouva (2)", TemplatesViewModel.BuildUniqueTemplateName("Pracovní smlouva", Names("pracovní smlouva")));
        }

        [Fact]
        public void SkipsNumbersAlreadyUsed()
        {
            Assert.Equal("Pokyn (4)", TemplatesViewModel.BuildUniqueTemplateName("Pokyn", Names("Pokyn", "Pokyn (2)", "Pokyn (3)")));
        }

        [Fact]
        public void UnknownFirmList_KeepsName()
        {
            Assert.Equal("Pokyn", TemplatesViewModel.BuildUniqueTemplateName("Pokyn", null));
        }
    }
}
