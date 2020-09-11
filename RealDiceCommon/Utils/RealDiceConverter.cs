using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace RealDiceCommon.Utils
{
    public static class RealDiceConverter
    {
        public static string Serialize<T>(T value)
        {
            return JsonConvert.SerializeObject(value);
        }

        public static T Deserialize<T>(string value)
        {
            return JsonConvert.DeserializeObject<T>(value);
        }
    }
}
