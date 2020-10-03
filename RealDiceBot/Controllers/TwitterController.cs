using BotFrameworkTwitterAdapter;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Newtonsoft.Json;
using System.IO;
using System.Threading;
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

        // https://docs.microsoft.com/ja-jp/azure/bot-service/rest-api/bot-framework-rest-direct-line-3-0-reconnect-to-conversation?view=azure-bot-service-4.0
        // ほか。。。Adapterの実装ガイダンスを見るべし。
        // 会話継続のためにDirectLineに戻ってきたメッセージは
        // BotFrameworkAdapter#ContinueConversationAsync で Activity#ServiceUrl にコールバックされる。
        // Slackなどの公式チャンネルではそれをチャネル固有のエンドポイントで受けて処理している。
        // サードパーティアダプタでこれをどうするのがいいのかはよく分からん。
        [HttpPost("v3/conversations/{conversationId}/activities/{activityId}")]
        public async Task PostV3ConversationsActivity(
            string conversationId,
            string activityId,
            CancellationToken cancellationToken)
        {
            // XXX 実装はアダプタの側にあるべきなんだろうな
            // XXX Validate
            string body;
            using (var sr = new StreamReader(Request.Body))
            {
                body = await sr.ReadToEndAsync();
            }
            var requestActivity = JsonConvert.DeserializeObject<Activity>(body);

            using (var turnContext = new TurnContext(_adapter, requestActivity))
            {
                var res = await _adapter.SendActivitiesAsync(turnContext, new Activity[] { requestActivity }, cancellationToken);
                // XXX { "id": "string" } を返してもいい。
                Response.StatusCode = 204;
            }
        }
    }
}
