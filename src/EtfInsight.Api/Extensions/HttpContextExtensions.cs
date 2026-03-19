using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Api.Extensions
{
    public static class HttpContextExtensions
    {
        public static Guid GetGuestId(this HttpContext ctx)
            => ctx.Items.TryGetValue(Middleware.GuestSessionMiddleware.GuestIdKey, out var raw)
                && raw is Guid parsed
                ? parsed
                : Guid.Empty;
    }
}