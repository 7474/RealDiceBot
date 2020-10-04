using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using RealDiceBot.Models.Api.WebChat;

namespace RealDiceBot.Controllers
{
    [Route("api/webchat")]
    [ApiController]
    public class WebChatController : ControllerBase
    {
        private readonly string secret;
        private const string dlUrl = "https://directline.botframework.com/v3/directline/tokens/generate";

        public WebChatController(IConfiguration configration)
        {
            // XXX WebChat固有のSiteでなくていい？
            secret = configration["DirectLineSecret"];
        }

        [HttpPost("token")]
        public async Task<ActionResult> Token()
        {
            HttpClient client = new HttpClient();
            var userId = $"dl_{Guid.NewGuid()}";

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, dlUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            request.Content = new StringContent(
                JsonConvert.SerializeObject(
                    new { User = new { Id = userId } }),
                    Encoding.UTF8,
                    "application/json");

            var response = await client.SendAsync(request);

            string token = String.Empty;
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                token = JsonConvert.DeserializeObject<DirectLineToken>(body).token;
            }

            var config = new ChatConfig()
            {
                Token = token,
                UserId = userId
            };

            return Ok(config);
        }
    }
}
