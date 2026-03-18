using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.DTOs;

namespace EtfInsight.Core.Interfaces
{
    public interface ICsvImportService
    {
        public Task<CsvImportResult> ImportAsync(Guid portfolioId, StreamReader reader, Guid userId = default, CancellationToken cancellationToken = default);
    }
}