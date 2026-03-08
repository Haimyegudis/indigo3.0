using IndiLogs_3._0.Services;
using System.Reflection;
using Xunit;

namespace IndiLogs.Tests
{
    public class AuthenticodeVerifierExtendedTests
    {
        // Access internal methods via reflection
        private static string? ParseDnField(string dn, string fieldName)
        {
            var method = typeof(AuthenticodeVerifier).GetMethod("ParseDnField",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            return (string?)method!.Invoke(null, new object[] { dn, fieldName });
        }

        private static bool IsTrustedSubject(string subject)
        {
            var method = typeof(AuthenticodeVerifier).GetMethod("IsTrustedSubject",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            return (bool)method!.Invoke(null, new object[] { subject })!;
        }

        // ── ParseDnField tests ──

        [Fact]
        public void ParseDnField_SimpleOrg_ExtractsValue()
        {
            var result = ParseDnField("CN=Test, O=HP Inc, C=US", "O");
            Assert.Equal("HP Inc", result);
        }

        [Fact]
        public void ParseDnField_QuotedOrg_ExtractsValue()
        {
            var result = ParseDnField("CN=Test, O=\"HP Inc.\", C=US", "O");
            Assert.Equal("HP Inc.", result);
        }

        [Fact]
        public void ParseDnField_AtStart_ExtractsValue()
        {
            var result = ParseDnField("O=HP, CN=Test", "O");
            Assert.Equal("HP", result);
        }

        [Fact]
        public void ParseDnField_NotPresent_ReturnsNull()
        {
            var result = ParseDnField("CN=Test, C=US", "O");
            Assert.Null(result);
        }

        [Fact]
        public void ParseDnField_EmptyDn_ReturnsNull()
        {
            Assert.Null(ParseDnField("", "O"));
        }

        [Fact]
        public void ParseDnField_NullDn_ReturnsNull()
        {
            Assert.Null(ParseDnField(null!, "O"));
        }

        [Fact]
        public void ParseDnField_FieldAtEnd_ExtractsValue()
        {
            var result = ParseDnField("CN=Test, O=Hewlett-Packard", "O");
            Assert.Equal("Hewlett-Packard", result);
        }

        [Fact]
        public void ParseDnField_SpaceAfterEquals_Trimmed()
        {
            var result = ParseDnField("O= HP Inc , C=US", "O");
            Assert.Equal("HP Inc", result);
        }

        [Fact]
        public void ParseDnField_PartialMatch_NotExtracted()
        {
            // "CO=HP" should not match "O="
            var result = ParseDnField("CO=HP, CN=Test", "O");
            Assert.Null(result);
        }

        [Fact]
        public void ParseDnField_CN_Extracted()
        {
            var result = ParseDnField("CN=TestCert, O=HP", "CN");
            Assert.Equal("TestCert", result);
        }

        [Fact]
        public void ParseDnField_EmptyValue_ReturnsNull()
        {
            // O= followed by nothing
            var result = ParseDnField("O=", "O");
            Assert.Null(result);
        }

        // ── IsTrustedSubject tests ──

        [Theory]
        [InlineData("CN=Test, O=HP, C=US", true)]
        [InlineData("CN=Test, O=HP Inc, C=US", true)]
        [InlineData("CN=Test, O=HP Inc., C=US", true)]
        [InlineData("CN=Test, O=Hewlett-Packard, C=US", true)]
        [InlineData("CN=Test, O=Hewlett Packard, C=US", true)]
        [InlineData("CN=Test, O=HP Indigo, C=US", true)]
        public void IsTrustedSubject_TrustedOrgs_ReturnsTrue(string subject, bool expected)
        {
            Assert.Equal(expected, IsTrustedSubject(subject));
        }

        [Theory]
        [InlineData("CN=Test, O=Microsoft, C=US")]
        [InlineData("CN=Test, O=Unknown Corp, C=US")]
        [InlineData("CN=Test, C=US")]
        [InlineData("")]
        public void IsTrustedSubject_UntrustedOrgs_ReturnsFalse(string subject)
        {
            Assert.False(IsTrustedSubject(subject));
        }

        [Fact]
        public void IsTrustedSubject_CaseInsensitive()
        {
            Assert.True(IsTrustedSubject("CN=Test, O=hp inc, C=US"));
            Assert.True(IsTrustedSubject("CN=Test, O=HP INC, C=US"));
        }

        [Fact]
        public void IsTrustedSubject_QuotedOrg_Trusted()
        {
            Assert.True(IsTrustedSubject("CN=Test, O=\"HP Inc.\", C=US"));
        }

        // ── VerifySignature with non-existent file ──

        [Fact]
        public void VerifySignature_NonExistentFile_ReturnsFalse()
        {
            var messages = new System.Collections.Generic.List<string>();
            var result = AuthenticodeVerifier.VerifySignature(
                @"C:\nonexistent\file.exe",
                msg => messages.Add(msg));

            Assert.False(result);
            Assert.True(messages.Count > 0);
        }
    }
}
