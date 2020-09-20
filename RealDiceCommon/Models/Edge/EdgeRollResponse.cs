using System;
using System.Collections.Generic;
using System.Text;

namespace RealDiceCommon.Models.Edge
{
    // Ref. RealDiceEdgeModule.Models
    public class EdgeRollResponse
    {
        public string Id { get; set; }
        public int Result { get; set; }
        public string PhotoName { get; set; }
        public string VideoName { get; set; }
    }
}
