namespace EtfInsight.Core.DTOs;

public sealed record IngestRequestDto
{
    public required string Ticker { get; init; }
    
    public required IReadOnlyList<IngestChunkDto> Chunks { get; init; }
}

public sealed record IngestChunkDto
{
    public required string Content { get; init; }
    
    public required float[] Embedding { get; init; }
    
    public required int ChunkIndex { get; init; }
    
    public required Dictionary<string, object> Metadata { get; init; } = new();
}