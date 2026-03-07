using IndiLogs_3._0.Services;
using Xunit;

namespace IndiLogs.Tests
{
    public class AuthenticodeVerifierTests
    {
        // ===================================================================
        // ParseDnField tests
        // ===================================================================

        [Fact]
        public void ParseDnField_SimpleValue_ReturnsField()
        {
            var result = AuthenticodeVerifier.ParseDnField("CN=Test, O=HP Inc, L=Rehovot", "O");
            Assert.Equal("HP Inc", result);
        }

        [Fact]
        public void ParseDnField_QuotedValue_ReturnsUnquoted()
        {
            var result = AuthenticodeVerifier.ParseDnField("CN=Test, O=\"HP Inc.\", L=Rehovot", "O");
            Assert.Equal("HP Inc.", result);
        }

        [Fact]
        public void ParseDnField_AtStart_ReturnsField()
        {
            var result = AuthenticodeVerifier.ParseDnField("O=HP, CN=Test", "O");
            Assert.Equal("HP", result);
        }

        [Fact]
        public void ParseDnField_AtEnd_ReturnsField()
        {
            var result = AuthenticodeVerifier.ParseDnField("CN=Test, O=HP Indigo", "O");
            Assert.Equal("HP Indigo", result);
        }

        [Fact]
        public void ParseDnField_MissingField_ReturnsNull()
        {
            var result = AuthenticodeVerifier.ParseDnField("CN=Test, L=Rehovot", "O");
            Assert.Null(result);
        }

        [Fact]
        public void ParseDnField_NullInput_ReturnsNull()
        {
            var result = AuthenticodeVerifier.ParseDnField(null!, "O");
            Assert.Null(result);
        }

        [Fact]
        public void ParseDnField_EmptyInput_ReturnsNull()
        {
            var result = AuthenticodeVerifier.ParseDnField("", "O");
            Assert.Null(result);
        }

        [Fact]
        public void ParseDnField_FieldNameSubstring_DoesNotMatch()
        {
            // "OU=HP" should not match when searching for "O"
            var result = AuthenticodeVerifier.ParseDnField("CN=Test, OU=HP", "O");
            Assert.Null(result);
        }

        [Fact]
        public void ParseDnField_CaseInsensitive_Matches()
        {
            var result = AuthenticodeVerifier.ParseDnField("cn=Test, o=HP Inc", "O");
            Assert.Equal("HP Inc", result);
        }

        [Fact]
        public void ParseDnField_SpaceAfterEquals_Trims()
        {
            var result = AuthenticodeVerifier.ParseDnField("O= HP Inc, CN=Test", "O");
            Assert.Equal("HP Inc", result);
        }

        [Fact]
        public void ParseDnField_CN_ReturnsCorrectly()
        {
            var result = AuthenticodeVerifier.ParseDnField("CN=MyApp, O=HP", "CN");
            Assert.Equal("MyApp", result);
        }

        // ===================================================================
        // IsTrustedSubject tests
        // ===================================================================

        [Theory]
        [InlineData("CN=Test, O=HP, L=Palo Alto")]
        [InlineData("CN=Test, O=HP Inc, L=Palo Alto")]
        [InlineData("CN=Test, O=HP Inc., L=Palo Alto")]
        [InlineData("CN=Test, O=Hewlett-Packard, L=Palo Alto")]
        [InlineData("CN=Test, O=Hewlett Packard, L=Palo Alto")]
        [InlineData("CN=Test, O=HP Indigo, L=Rehovot")]
        public void IsTrustedSubject_TrustedOrgs_ReturnsTrue(string subject)
        {
            Assert.True(AuthenticodeVerifier.IsTrustedSubject(subject));
        }

        [Theory]
        [InlineData("CN=Test, O=Microsoft, L=Redmond")]
        [InlineData("CN=Test, O=Unknown Corp, L=Somewhere")]
        [InlineData("")]
        public void IsTrustedSubject_UntrustedOrgs_ReturnsFalse(string subject)
        {
            Assert.False(AuthenticodeVerifier.IsTrustedSubject(subject));
        }

        [Fact]
        public void IsTrustedSubject_CaseInsensitive_ReturnsTrue()
        {
            Assert.True(AuthenticodeVerifier.IsTrustedSubject("CN=Test, O=hp inc, L=City"));
        }

        [Fact]
        public void IsTrustedSubject_QuotedOrg_ReturnsTrue()
        {
            Assert.True(AuthenticodeVerifier.IsTrustedSubject("CN=Test, O=\"HP Inc.\", L=City"));
        }

        [Fact]
        public void IsTrustedSubject_SubstringInCN_ReturnsFalse()
        {
            // HP appears in CN but not in O field
            Assert.False(AuthenticodeVerifier.IsTrustedSubject("CN=HP Printer Driver, O=SomeOtherCorp"));
        }
    }
}
