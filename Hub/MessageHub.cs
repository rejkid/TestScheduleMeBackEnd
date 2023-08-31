using log4net;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WebApi.Hub
{
    public class MessageHub : Hub<IMessageHubClient>
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        public async Task SendUpdatesToUser(int id)
        {
            log.InfoFormat("Before Clients.Caller.SendUpdate() called");
            await Clients.Caller.SendUpdate(id);
            log.InfoFormat("After Clients.Caller.SendUpdate() called");
        }
    }
}
