using System.Text.Json;
using CatalogSearch.Application;

namespace CatalogSearch.Infrastructure;

public static class IntakeRulePublication
{
    // The question set comes from the hash-verified original, not the possibly
    // replaced Result on a Completed review. No inferred or missing rows allowed.
    public static (IntakeEvent Answer, IntakeMappingResolution Resolution) Validate(
        IntakeReview review, IReadOnlyList<IntakeMappingQuestion> originalQuestions,
        string questionId, int answerVersion)
    {
        if (review.State is "Unknown" or "Running" or "Created")
            throw new ArgumentException("保存状態を照合してから再利用してください。");
        var selected = originalQuestions.Where(q => q.Id == questionId).ToArray();
        if (selected.Length != 1) throw new ArgumentException("原典の質問を確認できません。");
        var q = selected[0];
        var group = originalQuestions.Where(x => x.Scope == q.Scope && x.Kind == q.Kind && x.SourceKey == q.SourceKey).ToArray();
        if (group.Length == 0 || group.Select(x => x.Id).Distinct().Count() != group.Length)
            throw new ArgumentException("原典の質問集合が一意ではありません。");
        IntakeEvent? selectedEvent = null;
        IntakeMappingResolution? selectedResolution = null;
        string? meaning = null;
        foreach (var expected in group)
        {
            var events = review.History.Where(e => e.Method == "Human" && e.Action == "mapping" &&
                e.MappingResolutions?.Any(r => r.Question.Id == expected.Id) == true).ToArray();
            var latestVersion = events.Select(e => e.Version).DefaultIfEmpty(-1).Max();
            var latestEvents = events.Where(e => e.Version == latestVersion).ToArray();
            if (latestEvents.Length != 1 || latestVersion > review.Version)
                throw new ArgumentException("同じ列の全行について最新の回答を保存してください。");
            var latest = latestEvents[0];
            var answers = latest.MappingResolutions!.Where(r => r.Question.Id == expected.Id).ToArray();
            var current = review.MappingResolutions.Where(r => r.Question.Id == expected.Id).ToArray();
            if (answers.Length != 1 || current.Length != 1 ||
                JsonSerializer.Serialize(answers[0]) != JsonSerializer.Serialize(current[0]) ||
                JsonSerializer.Serialize(answers[0].Question) != JsonSerializer.Serialize(expected) ||
                !expected.Choices.Contains(answers[0].Meaning) || string.IsNullOrWhiteSpace(answers[0].Evidence))
                throw new ArgumentException("原典と最新の保存済み回答が一致しません。");
            if (meaning is not null && meaning != answers[0].Meaning)
                throw new ArgumentException("同じ列の全行を同じ意味で回答・保存してから再利用してください。");
            meaning = answers[0].Meaning;
            if (expected.Id == questionId) { selectedEvent = latest; selectedResolution = answers[0]; }
        }
        if (selectedEvent?.Version != answerVersion || selectedResolution is null)
            throw new ArgumentException("保存済みの最新の選択回答が必要です。");
        return (selectedEvent, selectedResolution);
    }
}
