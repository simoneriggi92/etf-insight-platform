using System;
using System.Collections.Generic;
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

        Task<ChatStreamResult> AskStreamAsync(
            string question,
            Guid userId,
            CancellationToken ct = default);
    }
}
