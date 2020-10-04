using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RealDiceBot.Models.Views
{

    public class ChatConfig
    {
        public string Token { get; set; }
        public string UserId { get; set; }
    }

    public class DirectLineToken
    {
        public string conversationId { get; set; }
        public string token { get; set; }
        public int expires_in { get; set; }
    }
}
