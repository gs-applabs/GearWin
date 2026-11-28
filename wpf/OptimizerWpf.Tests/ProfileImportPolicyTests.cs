using System;
using System.IO;
using System.Text;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class ProfileImportPolicyTests
{
    [Theory]
    [InlineData("Office", true)]
    [InlineData("Γραφείο 1", true)]
    [InlineData("../other", false)]
    [InlineData("CON", false)]
    [InlineData("LPT1.txt", false)]
    [InlineData("AUX", false)]
    [InlineData("name.", false)]
    [InlineData(" name", false)]
    [InlineData("a:b", false)]
    public void NamesAreNotSilentlySanitized(string name, bool expected) =>
        Assert.Equal(expected, ProfileImportPolicy.ValidName(name));

    [Fact]
    public void LegacyAndNewSchemaRemainReadable()
    {
        FullProfileData Parse(string json) => ProfileImportPolicy.Parse(Encoding.UTF8.GetBytes(json));
        Assert.True(Parse("{\"Main:StorageSense\":true}").Tweaks["Main:StorageSense"]);
        Assert.False(Parse("{\"Tweaks\":{\"Main:StorageSense\":false},\"Language\":\"el\"}").Tweaks["Main:StorageSense"]);
    }

    [Theory]
    [InlineData("{\"Tweaks\":null}")]
    [InlineData("{\"Tweaks\":{\"A\":true,\"a\":false}}")]
    [InlineData("{\"Tweaks\":{},\"Language\":\"invalid\"}")]
    [InlineData("{\"Tweaks\":{},\"MenuMode\":\"invalid\"}")]
    [InlineData("[]")]
    public void InvalidProfilesAreRefused(string json) =>
        Assert.Throws<InvalidDataException>(() => ProfileImportPolicy.Parse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void OversizeProfileRejectedBeforeParsing() =>
        Assert.Throws<InvalidDataException>(() => ProfileImportPolicy.Parse(new byte[ProfileImportPolicy.MaxBytes + 1]));

    [Fact]
    public void HibernateIsManualOnlyEvenForNamedProfiles()
    {
        var tweak = new SimpleTweak("Hibernate", "", () => { }, () => { }, Id: "Hibernate");
        Assert.False(ProfileImportPolicy.CanReplay(tweak));
        Assert.True(ProfileImportPolicy.RequiresOffConfirmation(tweak, false));
        Assert.False(ProfileImportPolicy.RequiresOffConfirmation(tweak, true));
    }
}
