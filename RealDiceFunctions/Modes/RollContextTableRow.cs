using RealDiceCommon.Models.Roll;
using System;
using System.Collections.Generic;
using System.Text;

namespace RealDiceFunctions.Modes
{
    public class RollContextTableRow : RollContext
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
    }
}
