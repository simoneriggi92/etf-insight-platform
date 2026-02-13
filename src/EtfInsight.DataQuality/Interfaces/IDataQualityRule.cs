using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.Entities;
using EtfInsight.DataQuality.Models;

namespace EtfInsight.DataQuality.Interfaces
{
    public interface IDataQualityRule
    {
        string RuleName { get; }
        Task<ValidationResult> ValidateAsync(EtfPrice etfPrice, EtfPrice? previousPrice);
    }
}