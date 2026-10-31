using LightSpace.Core.Config;
using LightSpace.Engine.Apps;

namespace LightSpace.Engine.Scenarios;

/// <summary>
/// Что играет в группе и почему.
/// </summary>
/// <param name="Scenario">Активный сценарий группы; null — играет «По умолчанию».</param>
/// <param name="Reason">Почему играет именно он: какое условие выполнено или почему не выполнено ни одно.</param>
public sealed record ScenarioChoice(ScenarioConfig? Scenario, string Reason);

/// <summary>
/// Выбор сценария группы: играет первый сценарий из её списка, у которого выполнено условие; если таких нет — «По умолчанию».
/// </summary>
public static class ScenarioSelector
{
    public static ScenarioChoice Choose(AppConfig config, GroupConfig group, AppWatcher apps)
    {
        foreach (var id in group.ScenarioIds)
        {
            var scenario = config.Scenarios.FirstOrDefault(s => s.Id == id);

            if (scenario == null)
                continue;

            var state = apps.StateOf(scenario.Condition);

            if (state != ConditionState.NotMet)
                return new ScenarioChoice(scenario, ReasonOf(scenario.Condition, state));
        }

        return new ScenarioChoice(null, group.ScenarioIds.Count == 0
            ? "У группы нет сценариев"
            : "Ни одно условие сценариев группы не выполнено");
    }

    /// <summary>
    /// Условие сценария словами: «dota2.exe · в фокусе»; без приложения — «Приложение не выбрано».
    /// </summary>
    public static string Describe(ScenarioCondition condition)
    {
        if (!condition.IsSet)
            return "Приложение не выбрано";

        return $"{condition.ExeName} · {(condition.Mode == ConditionMode.Focused ? "в фокусе" : "запущен")}";
    }

    private static string ReasonOf(ScenarioCondition condition, ConditionState state)
    {
        return state switch
        {
            ConditionState.Releasing => $"{condition.ExeName} ушёл с переднего плана, сценарий скоро снимется",
            _ when condition.Mode == ConditionMode.Focused => $"{condition.ExeName} в фокусе",
            _ => $"{condition.ExeName} запущен"
        };
    }
}
