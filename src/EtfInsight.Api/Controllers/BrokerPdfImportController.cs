using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using EtfInsight.Core.Interfaces;
using EtfInsight.Api.Extensions;

namespace EtfInsight.Api.Controllers
{
    [ApiController]
    [Route("api")]
    [Produces("application/json")]
    public class BrokerPdfImportController(IBrokerPdfImportService importService) : ControllerBase
    {
        private const int MaxFilesPerImport = 100;
        private const long MaxFileSizeBytes = 10 * 1024 * 1024;

        [HttpPost("portfolios/{portfolioId:guid}/import/broker-pdf")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> StartImport(
            Guid portfolioId,
            [FromForm] IFormCollection form,
            CancellationToken ct = default)
        {
            var userId = HttpContext.GetGuestId();

            if (form == null || form.Files.Count == 0)
                return BadRequest(new { Error = "At least one PDF file is required." });

            if (form.Files.Count > MaxFilesPerImport)
                return BadRequest(new
                    { Error = $"A maximum of {MaxFilesPerImport} files can be uploaded per import." });

            var invalids = form.Files
                .Where(f => f.Length == 0 || f.Length > MaxFileSizeBytes ||
                            !f.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                .Select(f => new { f.FileName, Size = f.Length })
                .ToList();

            if (invalids.Count > 0)
                return BadRequest(new { Error = "Invalid files (empty, >10 MB, or not .pdf).", Files = invalids });

            var result = await importService.StartImportAsync(portfolioId, userId, form.Files, ct);

            return result.Status == "not_found"
                ? NotFound(new { Error = $"Portfolio {portfolioId} not found or not owned by you." })
                : Accepted(result);
        }

        [HttpGet("import-jobs/{jobId:guid}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetJobStatus(Guid jobId, CancellationToken ct = default)
        {
            var userId = HttpContext.GetGuestId();
            var result = await importService.GetJobStatusAsync(jobId, userId, ct);

            return result == null
                ? NotFound(new { Error = $"Import job {jobId} not found or not owned by you." })
                : Ok(result);
        }

        [HttpGet("portfolios/{portfolioId:guid}/import-jobs")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetImportJobsForPortfolio(
            Guid portfolioId,
            CancellationToken ct = default)
        {
            var userId = HttpContext.GetGuestId();
            var jobs = importService.GetJobsByPortfolioIdAsync(portfolioId, userId, ct);

            return jobs is null
                ? NotFound(new { Error = $"Portfolio {portfolioId} not found or not owned by you." })
                : Ok(jobs);
        }

        [HttpGet("import-jobs/{jobId:guid}/items")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetImportJobDetail(
            Guid jobId,
            CancellationToken ct = default)
        {
            var userId = HttpContext.GetGuestId();
            var detail = await importService.GetJobDetailAsync(jobId, userId, ct);

            return detail is null
                ? NotFound(new { Error = $"Import job {jobId} not found or not owned by you." })
                : Ok(detail);
        }
    }
}