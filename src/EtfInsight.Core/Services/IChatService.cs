using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Core.DTOs;

namespace EtfInsight.Core.Services
{
    public interface IChatService
    {
        Task<ChatResponseDto> AskAiAsync(
            string question, 
            Guid userId, 
            CancellationToken ct = default);
    }
}