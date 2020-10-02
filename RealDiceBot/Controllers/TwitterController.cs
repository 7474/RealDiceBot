using BotFrameworkTwitterAdapter;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder;
using System.Threading.Tasks;

namespace Teams_Zoom_Sample.Controllers
{
    [Route("api/twitter")]
    [ApiController]
    public class TwitterController : ControllerBase
    {
        private readonly TwitterConversationAdapter _adapter;
        private readonly IBot _bot;

        public TwitterController(TwitterConversationAdapter adapter, IBot bot)
        {
            _adapter = adapter;
            _bot = bot;
        }

        [HttpPost]
        public async Task PostAsync()
        {
            // Delegate the processing of the HTTP POST to the adapter.
            // The adapter will invoke the bot.
            await _adapter.ProcessAsync(Request, Response, _bot);
        }
    }
}
