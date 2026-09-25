using Tapeory.Api.Data.Entities;
using Tapeory.Api.Rendering;

namespace Tapeory.Api.Tests.Unit;

public sealed class FieldValueValidatorTests
{
    private static TemplateField Field(string name, bool required = true, string? defaultValue = null) =>
        new() { Name = name, Required = required, DefaultValue = defaultValue };

    [Fact]
    public void Validate_IsValid_WhenAllRequiredFieldsHaveValues()
    {
        var fields = new[] { Field("name"), Field("sku") };
        var values = new Dictionary<string, string> { ["name"] = "Widget", ["sku"] = "W-1" };

        var result = FieldValueValidator.Validate(fields, values);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_IsInvalid_WhenARequiredFieldIsMissingAndHasNoDefault()
    {
        var fields = new[] { Field("name") };

        var result = FieldValueValidator.Validate(fields, new Dictionary<string, string>());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("name"));
    }

    [Fact]
    public void Validate_IsValid_WhenARequiredFieldIsMissingButHasADefault()
    {
        var fields = new[] { Field("name", defaultValue: "Unnamed") };

        var result = FieldValueValidator.Validate(fields, new Dictionary<string, string>());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_TreatsAWhitespaceOnlyValue_AsMissing()
    {
        var fields = new[] { Field("name") };
        var values = new Dictionary<string, string> { ["name"] = "   " };

        var result = FieldValueValidator.Validate(fields, values);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_IgnoresOptionalFields_EvenWithoutAValue()
    {
        var fields = new[] { Field("nickname", required: false) };

        var result = FieldValueValidator.Validate(fields, new Dictionary<string, string>());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ReportsEveryMissingField_NotJustTheFirst()
    {
        var fields = new[] { Field("a"), Field("b"), Field("c", required: false) };

        var result = FieldValueValidator.Validate(fields, new Dictionary<string, string>());

        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void ResolveValues_PrefersTheSubmittedValue_OverTheDefault()
    {
        var fields = new[] { Field("name", defaultValue: "Fallback") };
        var submitted = new Dictionary<string, string> { ["name"] = "Explicit" };

        var resolved = FieldValueValidator.ResolveValues(fields, submitted);

        Assert.Equal("Explicit", resolved["name"]);
    }

    [Fact]
    public void ResolveValues_FallsBackToTheDefault_WhenNotSubmitted()
    {
        var fields = new[] { Field("name", defaultValue: "Fallback") };

        var resolved = FieldValueValidator.ResolveValues(fields, new Dictionary<string, string>());

        Assert.Equal("Fallback", resolved["name"]);
    }

    [Fact]
    public void ResolveValues_FallsBackToEmptyString_WhenNeitherSubmittedNorDefaulted()
    {
        var fields = new[] { Field("name") };

        var resolved = FieldValueValidator.ResolveValues(fields, new Dictionary<string, string>());

        Assert.Equal(string.Empty, resolved["name"]);
    }

    [Fact]
    public void ResolveValues_TreatsAnEmptySubmittedValue_AsNotSubmitted()
    {
        var fields = new[] { Field("name", defaultValue: "Fallback") };
        var submitted = new Dictionary<string, string> { ["name"] = "" };

        var resolved = FieldValueValidator.ResolveValues(fields, submitted);

        Assert.Equal("Fallback", resolved["name"]);
    }
}
