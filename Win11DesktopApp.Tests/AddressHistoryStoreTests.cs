using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class AddressHistoryStoreTests
    {
        [Fact]
        public void Promote_PutsNewestFirstAndSkipsTheSameAddress()
        {
            var older = Address("Hlavní", "1", "Plzeň", "30100");
            var same = Address("hlavní", "1", "Plzeň", "30100");
            var other = Address("Dlouhá", "8", "Praha", "11000");

            var updated = AddressHistoryStore.Promote(new[] { older, other }, same);

            Assert.Equal(2, updated.Count);
            Assert.Equal("hlavní", updated[0].Street);
            Assert.Equal("Dlouhá", updated[1].Street);
        }

        [Fact]
        public void Promote_KeepsLocalAndAbroadListsIndependent()
        {
            var local = Address("Vodičkova", "2", "Praha", "11000");
            var abroad = Address("Rynek", "4", "Bielsko-Biała", "43-300");

            var localList = AddressHistoryStore.Promote(null, local);
            var abroadList = AddressHistoryStore.Promote(null, abroad);

            Assert.Single(localList);
            Assert.Equal("Praha", localList[0].City);
            Assert.Single(abroadList);
            Assert.Equal("Bielsko-Biała", abroadList[0].City);
        }

        private static MappedAddress Address(string street, string number, string city, string zip) =>
            new()
            {
                Street = street,
                Number = number,
                City = city,
                Zip = zip,
                Label = $"{street} {number}, {city}"
            };
    }
}
