namespace EtfInsight.Core.DTOs;

public sealed record ChatResponseDto
{
    public required string Answer { get; init; }
    
    public required IReadOnlyList<SearchResultDto> Sources { get; init; }
}

public sealed record SearchResultDto
{
    public required string Ticker { get; init; }
    
    public required string Content { get; init; }
    
    public required double Similarity { get; init; }
}