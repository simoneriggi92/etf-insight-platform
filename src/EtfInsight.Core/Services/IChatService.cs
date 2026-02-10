using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.Services
{
    public interface IChatService
    {
        Task<string> AskAiAsync(string question);
    }
}