namespace EtfInsight.Core.DTOs;

public sealed record SearchResult
{
    public required string Ticker { get; init; }
    public required string Content { get; init; }
    public required double Similarity { get; init; }
}