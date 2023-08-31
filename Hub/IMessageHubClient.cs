using System.Collections.Generic;
using System.Threading.Tasks;

namespace WebApi.Hub
{
    public interface IMessageHubClient
    {
        Task SendUpdate(int id);
    }
}
