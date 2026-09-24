namespace Faderio.Core.Domain;

/// <summary>One of a transition's four parameters, as the editor has a field for each.</summary>
public enum TransitionField
{
    FadeOutPoint,
    FadeOutDuration,
    FadeInPoint,
    FadeInDuration,
}

/// <summary>A value <c>Author</c> refused as impossible, and why, worded for the user beside
/// the field it names (build issue 14). A refused spec is not stored.</summary>
public sealed record Refusal(TransitionField Field, string Reason);
