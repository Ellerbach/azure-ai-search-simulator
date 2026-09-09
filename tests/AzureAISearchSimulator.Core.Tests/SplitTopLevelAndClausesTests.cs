using Xunit;
using AzureAISearchSimulator.Search;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Direct unit tests for SearchService.SplitTopLevelAndClauses - the quote-aware replacement
/// for the old `filter.Split(" and ")`, which used to split a string literal containing the
/// word "and" (e.g. inside `tags/any(t: t eq 'bed and breakfast')`) into unparseable fragments.
/// </summary>
public class SplitTopLevelAndClausesTests
{
    [Fact]
    public void NoAndKeyword_ReturnsSingleClauseUnchanged()
    {
        var result = SearchService.SplitTopLevelAndClauses("category eq 'Luxury'");

        Assert.Equal(new[] { "category eq 'Luxury'" }, result);
    }

    [Fact]
    public void LowercaseAnd_SplitsIntoTwoClauses()
    {
        var result = SearchService.SplitTopLevelAndClauses("category eq 'Luxury' and rating gt 4");

        Assert.Equal(new[] { "category eq 'Luxury'", "rating gt 4" }, result);
    }

    [Fact]
    public void UppercaseAND_SplitsIntoTwoClauses()
    {
        var result = SearchService.SplitTopLevelAndClauses("category eq 'Luxury' AND rating gt 4");

        Assert.Equal(new[] { "category eq 'Luxury'", "rating gt 4" }, result);
    }

    [Fact]
    public void MixedCaseAnd_IsNotRecognizedAsASeparator()
    {
        // Matches the original filter.Split(" and ", " AND ") behavior: only those two exact
        // casings are separators, so "And"/"aNd" are left as part of the (single) clause text.
        var result = SearchService.SplitTopLevelAndClauses("category eq 'Luxury' And rating gt 4");

        Assert.Equal(new[] { "category eq 'Luxury' And rating gt 4" }, result);
    }

    [Fact]
    public void ThreeClauses_SplitsIntoThreeParts()
    {
        var result = SearchService.SplitTopLevelAndClauses("a eq 1 and b eq 2 and c eq 3");

        Assert.Equal(new[] { "a eq 1", "b eq 2", "c eq 3" }, result);
    }

    [Fact]
    public void AndInsideQuotedLiteral_IsNotSplit()
    {
        var result = SearchService.SplitTopLevelAndClauses("tags/any(t: t eq 'bed and breakfast')");

        Assert.Equal(new[] { "tags/any(t: t eq 'bed and breakfast')" }, result);
    }

    [Fact]
    public void AndInsideQuotedLiteral_FollowedByTopLevelAnd_SplitsOnlyTheTopLevelOne()
    {
        var result = SearchService.SplitTopLevelAndClauses("tags/any(t: t eq 'bed and breakfast') and rating gt 4");

        Assert.Equal(new[] { "tags/any(t: t eq 'bed and breakfast')", "rating gt 4" }, result);
    }

    [Fact]
    public void EscapedQuoteInsideLiteral_DoesNotEndTheLiteralEarly()
    {
        // OData escapes a literal single quote inside a string as '' (doubled). The "and" between
        // "O''Brien" and "Co" must stay inside the literal despite the doubled quote right before it.
        var result = SearchService.SplitTopLevelAndClauses("name eq 'O''Brien and Co' and rating gt 4");

        Assert.Equal(new[] { "name eq 'O''Brien and Co'", "rating gt 4" }, result);
    }

    [Fact]
    public void EmptyString_ReturnsEmptyList()
    {
        var result = SearchService.SplitTopLevelAndClauses("");

        Assert.Empty(result);
    }

    [Fact]
    public void WhitespaceOnly_ReturnsEmptyList()
    {
        var result = SearchService.SplitTopLevelAndClauses("   ");

        Assert.Empty(result);
    }
}
