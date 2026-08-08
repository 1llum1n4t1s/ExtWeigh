using ExtWeigh.Core.Models;

namespace ExtWeigh.Tests;

[TestClass]
public sealed class ScenarioTests
{
    private static Scenario Named(string name) => new() { Name = name, Url = "https://example.com/" };

    [TestMethod]
    public void Slug_日本語のみの名前でも衝突しない()
    {
        // ASCII 英数字が 1 文字も無い名前はスラグ本体が同じ "scenario" に潰れるため、
        // 連番が無いと出力ディレクトリが衝突して計測結果が上書きされる。
        var first = Named("朝の巡回").Slug(0);
        var second = Named("夜の巡回").Slug(1);

        Assert.AreNotEqual(first, second);
        Assert.AreEqual("01-scenario", first);
        Assert.AreEqual("02-scenario", second);
    }

    [TestMethod]
    public void Slug_同名のシナリオでも位置で区別される()
    {
        Assert.AreNotEqual(Named("普段使うページ 1").Slug(0), Named("普段使うページ 1").Slug(1));
    }

    [TestMethod]
    public void Slug_ASCII部分は読める形で残る()
    {
        Assert.AreEqual("01-youtube", Named("YouTubeを読み進める").Slug(0));
        Assert.AreEqual("03-browse-top", Named("Browse Top").Slug(2));
    }
}
