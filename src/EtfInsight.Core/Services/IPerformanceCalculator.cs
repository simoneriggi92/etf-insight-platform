using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.Entities;

namespace EtfInsight.Core.Services
{
    public interface IPerformanceCalculator
    {
        /// <summary>
        /// Calculate the TWRR yearly and periodic
        /// </summary>
        /// <param name="transactions"></param>
        /// <param name="etfPrices"></param>
        /// <returns></returns>
        public Task<decimal> CalculateTWRR(Guid portfolioId, DateOnly from, DateOnly to);
    }
}