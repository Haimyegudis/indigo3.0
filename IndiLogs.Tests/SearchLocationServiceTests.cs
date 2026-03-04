using System;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services.Grep;
using Xunit;

namespace IndiLogs.Tests
{
    public class SearchLocationServiceTests
    {
        // ── BuildUncPath ──

        [Theory]
        [InlineData("192.168.1.1", "Logs$", @"\\192.168.1.1\Logs$")]
        [InlineData("myserver", "SharedFolder", @"\\myserver\SharedFolder")]
        [InlineData(" 10.0.0.1 ", "  Share  ", @"\\10.0.0.1\  Share  ")]
        public void BuildUncPath_ValidInputs_ReturnsCorrectPath(string address, string share, string expected)
        {
            var result = SearchLocationService.BuildUncPath(address, share);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(null, "share")]
        [InlineData("", "share")]
        [InlineData("  ", "share")]
        [InlineData("host", null)]
        [InlineData("host", "")]
        [InlineData("host", "  ")]
        [InlineData(null, null)]
        public void BuildUncPath_NullOrWhitespace_ReturnsNull(string? address, string? share)
        {
            Assert.Null(SearchLocationService.BuildUncPath(address, share));
        }

        [Fact]
        public void BuildUncPath_TrimsAddress()
        {
            var result = SearchLocationService.BuildUncPath(" 10.0.0.1 ", "Logs");
            Assert.NotNull(result);
            Assert.StartsWith(@"\\10.0.0.1", result);
        }

        [Fact]
        public void BuildUncPath_TrimsLeadingBackslashesFromShare()
        {
            var result = SearchLocationService.BuildUncPath("server", @"\\Share");
            Assert.NotNull(result);
            Assert.Equal(@"\\server\Share", result);
        }

        // ── CRUD operations ──

        [Fact]
        public void Add_IncreasesCount()
        {
            var service = new SearchLocationService();
            int before = service.Locations.Count;
            var loc = new SearchLocation { Name = "Test", Address = "127.0.0.1", BasePath = @"C:\Temp" };
            service.Add(loc);
            Assert.Equal(before + 1, service.Locations.Count);
            // Cleanup
            service.Remove(loc.Id);
        }

        [Fact]
        public void Remove_DecreasesCount()
        {
            var service = new SearchLocationService();
            var loc = new SearchLocation { Name = "ToRemove", Address = "127.0.0.1" };
            service.Add(loc);
            int countAfterAdd = service.Locations.Count;
            service.Remove(loc.Id);
            Assert.Equal(countAfterAdd - 1, service.Locations.Count);
        }

        [Fact]
        public void Update_ModifiesExistingLocation()
        {
            var service = new SearchLocationService();
            var loc = new SearchLocation { Name = "Original", Address = "1.2.3.4" };
            service.Add(loc);
            loc.Name = "Updated";
            service.Update(loc);
            var found = service.Locations[service.Locations.Count - 1];
            Assert.Equal("Updated", found.Name);
            // Cleanup
            service.Remove(loc.Id);
        }

        [Fact]
        public void Remove_NonExistentId_DoesNotThrow()
        {
            var service = new SearchLocationService();
            service.Remove(Guid.NewGuid()); // Should not throw
        }
    }
}
