using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.Mathematics
{
    public static class RoundingPolicy
    {
        public static decimal Money(decimal value)
        => System.Math.Round(value, 2, MidpointRounding.AwayFromZero);

        public static decimal Ratio(decimal value)
        => System.Math.Round(value, 4, MidpointRounding.AwayFromZero);
    }
}