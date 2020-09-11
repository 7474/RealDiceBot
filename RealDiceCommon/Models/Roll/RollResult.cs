using System;
using System.Collections.Generic;
using System.Text;

namespace RealDiceCommon.Models.Roll
{
    public class RollResult
    {
        public RollRequest Request { get; set; }
        public uint[] Results { get; set; }
    }
}
