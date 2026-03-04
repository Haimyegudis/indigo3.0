using System;
using IndiLogs_3._0.Services;
using Xunit;

namespace IndiLogs.Tests
{
    public class UpdateServiceTests
    {
        [Fact]
        public void Constructor_WithDialogService_DoesNotThrow()
        {
            var dialogService = new TestDialogService();
            var service = new UpdateService(dialogService);
            Assert.NotNull(service);
        }

        [Fact]
        public void Constructor_WithNull_DoesNotThrow()
        {
            var service = new UpdateService(null);
            Assert.NotNull(service);
        }

        // Version comparison logic tests
        [Theory]
        [InlineData("1.0.0", "1.0.1", true)]
        [InlineData("1.0.0", "2.0.0", true)]
        [InlineData("1.0.0", "1.0.0", false)]
        [InlineData("2.0.0", "1.0.0", false)]
        [InlineData("1.2.3", "1.2.4", true)]
        [InlineData("1.2.3", "1.3.0", true)]
        public void Version_Comparison_IsCorrect(string current, string server, bool serverIsNewer)
        {
            var currentVersion = Version.Parse(current);
            var serverVersion = Version.Parse(server);
            Assert.Equal(serverIsNewer, serverVersion > currentVersion);
        }

        [Theory]
        [InlineData("1.0.0", true)]
        [InlineData("10.2.3.4", true)]
        [InlineData("abc", false)]
        [InlineData("", false)]
        [InlineData("1.0.0.0.0", false)]
        public void Version_TryParse_HandlesEdgeCases(string input, bool expectedSuccess)
        {
            bool result = Version.TryParse(input, out _);
            Assert.Equal(expectedSuccess, result);
        }
    }
}
