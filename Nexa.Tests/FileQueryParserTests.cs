using FluentAssertions;
using Xunit;
using Nexa.Windows.FileSearch;

namespace Nexa.Tests;

public class FileQueryParserTests
{
    [Fact]
    public void Parse_ExtractsNameTerms()
    {
        var query = FileQueryParser.Parse("find files named invoice");

        query.NameTerms.Should().Contain("invoice");
        query.HasMeaningfulCriteria.Should().BeTrue();
    }

    [Fact]
    public void Parse_ExtractsPdfExtensionFromWord()
    {
        var query = FileQueryParser.Parse("find newest pdf");

        query.Extensions.Should().Contain(".pdf");
        query.RecencySort.Should().Be(FileQuery.RecencyMode.NewestFirst);
    }

    [Fact]
    public void Parse_ExtractsExtensionFromFileName()
    {
        var query = FileQueryParser.Parse("find report.xlsx");

        query.Extensions.Should().Contain(".xlsx");
    }

    [Fact]
    public void Parse_RecognizesExcelAlias()
    {
        var query = FileQueryParser.Parse("excel file i downloaded yesterday");

        query.Extensions.Should().Contain(".xlsx");
        query.MaximumAge.Should().Be(2);
        query.RecencySort.Should().Be(FileQuery.RecencyMode.NewestFirst);
    }

    [Fact]
    public void Parse_RecognizesDownloadsAge()
    {
        var query = FileQueryParser.Parse("downloaded 3 days ago");

        query.MaximumAge.Should().Be(4);
        query.RecencySort.Should().Be(FileQuery.RecencyMode.NewestFirst);
    }

    [Fact]
    public void Parse_RecognizesLatestRecency()
    {
        var query = FileQueryParser.Parse("latest screenshot");

        query.Extensions.Should().Contain(".png");
        query.RecencySort.Should().Be(FileQuery.RecencyMode.NewestFirst);
    }

    [Fact]
    public void Parse_StripsNoiseWords()
    {
        var query = FileQueryParser.Parse("show me the files called budget");

        query.NameTerms.Should().Contain("budget");
        query.NameTerms.Should().NotContain("show");
        query.NameTerms.Should().NotContain("the");
    }

    [Fact]
    public void Parse_EmptyQuery_HasNoCriteria()
    {
        var query = FileQueryParser.Parse("");

        query.HasMeaningfulCriteria.Should().BeFalse();
    }

    [Fact]
    public void Parse_HandlesCaseInsensitiveTerms()
    {
        var query = FileQueryParser.Parse("FIND NEWEST PDF");

        query.Extensions.Should().Contain(".pdf");
        query.RecencySort.Should().Be(FileQuery.RecencyMode.NewestFirst);
    }
}