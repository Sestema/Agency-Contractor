using System;
using Win11DesktopApp.Helpers;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class MoveDateCheckTests
    {
        private static readonly DateTime Today = new(2026, 11, 26);

        [Theory]
        [InlineData("26.11.2026", "26.11.2026")]
        [InlineData(" 6.1.2026 ", "06.01.2026")]
        [InlineData("26.11.26", "26.11.2026")]
        [InlineData("2026-11-26", "26.11.2026")]
        [InlineData("26/11/2026", "26.11.2026")]
        public void ReadableDates_AreNormalized(string text, string expected)
        {
            Assert.True(MoveDateCheck.TryNormalize(text, out var normalized, out _));
            Assert.Equal(expected, normalized);
        }

        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("26.11")]
        [InlineData("32.11.2026")]
        [InlineData("26.13.2026")]
        [InlineData("26.11.20261")]
        public void UnreadableDates_AreRefused(string text)
        {
            Assert.False(MoveDateCheck.TryNormalize(text, out _, out _));
        }

        [Fact]
        public void Archive_EndBeforeStart_OnlyWarns()
        {
            var warnings = MoveDateCheck.ForArchive(new DateTime(2026, 11, 26), new DateTime(2026, 12, 1), Today);
            Assert.Equal(new[] { MoveDateCheck.Warning.EndBeforeStart }, warnings);
            Assert.Empty(MoveDateCheck.ForArchive(new DateTime(2026, 12, 1), new DateTime(2026, 12, 1), Today));
        }

        [Fact]
        public void Restore_StartBeforeArchiveEnd_AndFarDates_Warn()
        {
            Assert.Equal(new[] { MoveDateCheck.Warning.StartBeforeArchiveEnd },
                MoveDateCheck.ForRestore(new DateTime(2026, 11, 20), new DateTime(2026, 11, 25), Today));
            Assert.Empty(MoveDateCheck.ForRestore(new DateTime(2026, 11, 25), new DateTime(2026, 11, 25), Today));
            Assert.Equal(new[] { MoveDateCheck.Warning.FarFromToday },
                MoveDateCheck.ForRestore(new DateTime(2062, 11, 26), null, Today));
        }
    }
}
