using System;
using System.Security.Cryptography.X509Certificates;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class DriverSignatureTests
{
    [Theory]
    [InlineData("Valid", "CN=Microsoft Windows, O=Microsoft Corporation, C=US", true)]
    [InlineData("Valid", "CN=Unrelated, O=Other Corp", false)]
    [InlineData("Valid", "CN=Microsoft Fake, O=NotMicrosoft Corporation", false)]
    [InlineData("HashMismatch", "CN=Microsoft Windows, O=Microsoft Corporation", false)]
    [InlineData("Valid", "CN=\"Fake, O=Microsoft Corporation\", O=Other Corp", false)]
    [InlineData("Valid", "CN=Microsoft Corporation", false)]
    [InlineData("Valid", "CN=Test, O=Microsoft Corporation, O=Other Corp", false)]
    public void SignatureRequiresValidStatusAndActualOrganization(string status, string dn, bool expected)
    {
        var subject = new X500DistinguishedName(dn);
        Assert.Equal(expected, DriverService.IsTrustedMicrosoftSignature(status + "|" + Convert.ToBase64String(subject.RawData)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Valid")]
    [InlineData("Valid|not base64")]
    [InlineData("Valid|AA==")]
    [InlineData("Valid|CN=Microsoft Windows, O=Microsoft Corporation")]
    public void InvalidProofIsRejected(string? output) =>
        Assert.False(DriverService.IsTrustedMicrosoftSignature(output));
}
