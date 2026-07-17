using ExtWeigh.Core.Models;
using ExtWeigh.UI.ViewModels;

namespace ExtWeigh.Tests;

[TestClass]
public sealed class ScenarioItemViewModelTests
{
    [TestMethod]
    public void 通常閲覧_実行内容と実時間の目安を表示する()
    {
        var scenario = new ScenarioItemViewModel
        {
            Name = "短時間の閲覧",
            Url = "https://example.com/",
            DurationSec = 10,
        };

        Assert.IsFalse(scenario.IsShortcutScenario);
        Assert.AreEqual("ページを読み進める", scenario.ScenarioTypeLabel);
        StringAssert.Contains(scenario.ExecutionDescription, "2,000px スクロール");
        Assert.AreEqual("実行時間の目安: 約17.5秒", scenario.EstimatedDurationDescription);

        var plan = scenario.ToScenario();
        Assert.AreEqual(5, plan.Steps.Count);
        Assert.AreEqual(StepType.Scroll, plan.Steps[1].Type);
    }

    [TestMethod]
    public void ショートカット_専用の実行内容と実時間の目安を表示する()
    {
        var scenario = new ScenarioItemViewModel
        {
            Name = "ショートカットを試す",
            Url = "https://example.com/",
            DurationSec = 10,
            Shortcut = "Ctrl+Shift+Y",
        };

        Assert.IsTrue(scenario.IsShortcutScenario);
        Assert.AreEqual("ショートカットを試す", scenario.ScenarioTypeLabel);
        StringAssert.Contains(scenario.ExecutionDescription, "Ctrl+Shift+Y を押す");
        Assert.AreEqual("実行時間の目安: 約10秒", scenario.EstimatedDurationDescription);

        var plan = scenario.ToScenario();
        Assert.AreEqual(2, plan.Steps.Count);
        Assert.AreEqual(StepType.Keyboard, plan.Steps[1].Type);
        Assert.AreEqual("Ctrl+Shift+Y", plan.Steps[1].Shortcut);
    }
}
