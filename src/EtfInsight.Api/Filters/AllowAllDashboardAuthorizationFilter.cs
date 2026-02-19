using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hangfire.Dashboard;

namespace EtfInsight.Api.Filters
{
    /// <summary>
    /// FOR LOCAL DEV ONLY: Allows all users to access the Hangfire dashboard without authentication.
    /// In production, replace with a proper authorization filter that checks user roles/claims.
    /// </summary>
    public class AllowAllDashboardAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            return true;
        }
    }
}