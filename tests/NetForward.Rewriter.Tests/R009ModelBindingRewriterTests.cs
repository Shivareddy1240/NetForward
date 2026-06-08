using FluentAssertions;
using NetForward.Analyzer;
using NetForward.Rewriter.Rewriters;
using Xunit;

namespace NetForward.Rewriter.Tests;

public class R009ModelBindingRewriterTests
{
    private readonly R009ModelBindingRewriter _rule = new();

    [Fact]
    public async Task Converts_Bind_Include_to_positional_syntax()
    {
        var source = @"
public class FooController
{
    public IActionResult Create([Bind(Include = ""Name,Email"")] UserModel model)
    {
        return Ok();
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("[Bind(\"Name,Email\")]");
        output.Should().NotContain("Include =");
        result.Transformations.Should().Contain(t =>
            t.Description.Contains("Bind") && t.RuleId == "R009");
    }

    [Fact]
    public async Task Replaces_FormCollection_with_IFormCollection()
    {
        var source = @"
public class FooController
{
    public IActionResult Submit(FormCollection form)
    {
        var val = form[""key""];
        return Ok();
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        // The parameter type is now IFormCollection.
        output.Should().Contain("IFormCollection form");

        // The legacy bare type "FormCollection" (without "I" prefix) is gone.
        // We check by asserting "FormCollection " doesn't appear as a standalone type.
        output.Should().NotContain("(FormCollection ");
        result.Transformations.Should().Contain(t =>
            t.Description.Contains("IFormCollection"));
    }

    [Fact]
    public async Task Replaces_HttpPostedFileBase_with_IFormFile()
    {
        var source = @"
public class UploadController
{
    public IActionResult Upload(HttpPostedFileBase file)
    {
        return Ok(file.FileName);
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("IFormFile");
        output.Should().NotContain("HttpPostedFileBase");
        result.Transformations.Should().Contain(t => t.Description.Contains("IFormFile"));
    }

    [Fact]
    public async Task Replaces_HttpPostedFilesBase_with_IFormFileCollection()
    {
        var source = @"
public class UploadController
{
    public IActionResult UploadMany(HttpPostedFilesBase files)
    {
        return Ok();
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("IFormFileCollection");
        output.Should().NotContain("HttpPostedFilesBase");
    }

    [Fact]
    public async Task Raises_NF402_for_UpdateModel()
    {
        var source = @"
public class FooController
{
    public IActionResult Edit(UserModel model)
    {
        UpdateModel(model);
        return Ok();
    }
}";
        var (_, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        result.Issues.Should().Contain(i =>
            i.Id == IssueIds.AsyncSignatureRequired
            && i.Title.Contains("UpdateModel"));
    }

    [Fact]
    public async Task Raises_NF402_for_TryUpdateModel()
    {
        var source = @"
public class FooController
{
    public IActionResult Edit(UserModel model)
    {
        if (TryUpdateModel(model))
            return Ok();
        return BadRequest();
    }
}";
        var (_, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        result.Issues.Should().Contain(i =>
            i.Id == IssueIds.AsyncSignatureRequired
            && i.Title.Contains("TryUpdateModel"));
    }

    [Fact]
    public async Task Does_not_modify_already_modern_patterns()
    {
        var source = @"
public class FooController
{
    public IActionResult Create(
        [Bind(""Name,Email"")] UserModel model,
        IFormCollection form,
        IFormFile file)
    {
        return Ok();
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Be(source);
        result.Transformations.Should().BeEmpty();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void IsApplicable_returns_true_for_legacy_HttpPostedFileBase()
    {
        var tree = RuleTestHelpers.ParseSource("class C { void M(HttpPostedFileBase x) {} }");
        _rule.IsApplicable(tree).Should().BeTrue();
    }

    [Fact]
    public void IsApplicable_returns_true_for_legacy_UpdateModel()
    {
        // UpdateModel as an invocation
        var tree = RuleTestHelpers.ParseSource("class C { void M() { UpdateModel(null); } }");
        _rule.IsApplicable(tree).Should().BeTrue();
    }

    [Fact]
    public void IsApplicable_returns_true_for_legacy_FormCollection()
    {
        var tree = RuleTestHelpers.ParseSource("class C { void M(FormCollection form) {} }");
        _rule.IsApplicable(tree).Should().BeTrue();
    }

    [Fact]
    public void IsApplicable_returns_false_for_modern_binding()
    {
        // IFormCollection and IFormFile are modern — should NOT trigger the rule.
        var tree = RuleTestHelpers.ParseSource(
            "class C { void M(IFormCollection form, IFormFile file) {} }");
        _rule.IsApplicable(tree).Should().BeFalse();
    }
}
