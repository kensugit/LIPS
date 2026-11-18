namespace CatalogSearch.Application;

public sealed record IntakeMappingQuestion(string Id, IntakeRuleScope Scope, string Kind, string SourceKey,
    string Prompt, string Sheet, int Row, string Sku, string RawValue, IReadOnlyList<string> Choices);
public sealed record IntakeMappingAnswer(string QuestionId, string Meaning);
public sealed record IntakeMappingResolution(IntakeMappingQuestion Question, string Meaning, string Evidence);
