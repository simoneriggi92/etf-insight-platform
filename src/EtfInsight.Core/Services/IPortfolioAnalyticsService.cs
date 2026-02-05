using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.DTOs;

namespace EtfInsight.Core.Services
{
    public interface IPortfolioAnalyticsService : IPerformanceCalculator
    {
        Task<PortfolioDashboardDto> GetPortfolioAnalyticsAsync(Guid portfolioId, DateOnly from, DateOnly to);
    }
}