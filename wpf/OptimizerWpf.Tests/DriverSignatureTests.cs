using OptimizerWpf.Services;
namespace OptimizerWpf.Tests
{
    public sealed class DriverSignatureTests
    {
        [Theory]
        [InlineData("Valid|CN=Microsoft Windows, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", true)]
        [InlineData("Valid|CN=Evil, O=Evil Corp, C=US", false)]
        [InlineData("Valid|CN=Microsoft Fake, O=NotMicrosoft Corporation", false)]
        [InlineData("HashMismatch|CN=Microsoft Windows, O=Microsoft Corporation", false)]
        [InlineData("NotSigned|", false)]
        [InlineData("Valid", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void OnlyValidMicrosoftSignaturesAreTrusted(string? output, bool expected) =>
            Assert.Equal(expected, DriverService.IsTrustedMicrosoftSignature(output));
    }
}
