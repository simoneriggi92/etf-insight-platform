using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Api.Middleware
{
    public class GuestSessionMiddleware(RequestDelegate next)
    {
         public const string GuestIdKey = "GuestUserId";

        public async Task InvokeAsync(HttpContext ctx)
        {
            if (ctx.Request.Headers.TryGetValue("X-Guest-ID", out var raw)
                && Guid.TryParse(raw, out var parsed))
            {
                ctx.Items[GuestIdKey] = parsed;
            }
            else
            {
                // Auto-generate so downstream code always has a non-null id.
                // The client receives it back in the response header and should
                // persist it in localStorage.
                var generated = Guid.NewGuid();
                ctx.Items[GuestIdKey] = generated;
                ctx.Response.Headers["X-Guest-ID"] = generated.ToString();
            }

            await next(ctx);
        }
    }
}