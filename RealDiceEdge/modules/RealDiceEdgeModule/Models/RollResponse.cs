using System;
using System.Collections.Generic;
using System.Text;

namespace RealDiceEdgeModule.Models
{
    class RollResponse
    {
        public string Id { get; set; }
        public string Status { get; set; }
        public int Result { get; set; }
        public double Score { get; set; }
        public string PhotoName { get; set; }
        public string VideoName { get; set; }
    }
}
