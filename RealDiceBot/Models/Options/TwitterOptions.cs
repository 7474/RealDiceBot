using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RealDiceBot.Models.Options
{
    public class TwitterOptions
    {
        public string ConsumerKey { get; set; }
        public string ConsumerSecret { get; set; }
        public string BearerToken { get; set; }
        public string AccessToken { get; set; }
        public string AccessTokenSecret { get; set; }
    }
}
