using System;
using System.Collections.Generic;
using System.Text;

namespace RealDiceCommon.Models.Roll
{
    public class RollContext
    {
        public string Id { get; set; }
        public IDictionary<string, string> MetaData { get; set; }

        public IList<RollRequest> Requests { get; set; }
        public IList<RollResult> Results { get; set; }
    }
}
