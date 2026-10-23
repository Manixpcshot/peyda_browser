using System.Reflection;
using PeydaBrowser.Core.Services;

namespace PeydaBrowser.UnitTests;

public class LocalizationTests
{
    [Fact]
    public void EnglishAndPersian_HaveIdenticalKeySets()
    {
        var loc = new LocalizationService();
        var en = loc.GetKeys("en").ToHashSet(StringComparer.Ordinal);
        var fa = loc.GetKeys("fa").ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(en);
        Assert.Equal(en, fa);
    }

    [Fact]
    public void EveryLocKeyConstant_ExistsInBothLanguages()
    {
        var loc = new LocalizationService();
        var en = loc.GetKeys("en").ToHashSet(StringComparer.Ordinal);
        var fa = loc.GetKeys("fa").ToHashSet(StringComparer.Ordinal);
        var constants = typeof(LocKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(constants);
        Assert.All(constants, key =>
        {
            Assert.Contains(key, en);
            Assert.Contains(key, fa);
        });
    }

    [Fact]
    public void Persian_IsRightToLeft_WithFaIrCulture()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("fa");
        Assert.True(loc.IsRightToLeft);
        Assert.Equal("fa-IR", loc.Culture.Name);
        Assert.Equal("تنظیمات", loc[LocKeys.SettingsTitle]);
        Assert.Equal("Peyda Browser", loc[LocKeys.AppName]);
    }

    [Fact]
    public void UnknownLanguage_FallsBackToEnglish()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("de");
        Assert.Equal("en", loc.CurrentLanguage);
        Assert.False(loc.IsRightToLeft);
    }

    [Fact]
    public void MissingKey_ReturnsKeyItself_FormatWorks()
    {
        var loc = new LocalizationService();
        Assert.Equal("no.such.key", loc["no.such.key"]);
        var text = loc.Format(LocKeys.AboutVersion, "1.2.3");
        Assert.Contains("1.2.3", text);
    }
}
