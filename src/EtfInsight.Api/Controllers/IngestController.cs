using EtfInsight.Api.Attributes;
using EtfInsight.Core.DTOs;
using EtfInsight.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EtfInsight.Api.Controllers;

[ApiController]
[Route("api/search")]
[Produces("application/json")]
public sealed class IngestController : ControllerBase
{
    private readonly ISemanticSearchRepository _repository;
    private readonly ILogger<IngestController> _logger;

    public IngestController(ISemanticSearchRepository repository, ILogger<IngestController> logger)
    {
        _repository = repository;
        _logger = logger;
    }
    
    [HttpPost ("ingest")]
    [ApiKeyRequired]
    public async Task<IActionResult> Ingest(
        [FromBody] IngestRequestDto request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        
        if (string.IsNullOrWhiteSpace(request.Ticker))
            return BadRequest(new { error = "Ticker is required" });

        if (request.Chunks.Count == 0)
            return BadRequest(new { error = "At least one chunk is required" });

        _logger.LogInformation("Ingesting {Count} chunks for {Ticker}", request.Chunks.Count, request.Ticker);

        await _repository.BulkReplaceAsync(
            request.Ticker,
            request.Chunks,
            ct);

        return Ok(new
        {
            ticker = request.Ticker,
            chucksIngested = request.Chunks.Count
        });
    }
}