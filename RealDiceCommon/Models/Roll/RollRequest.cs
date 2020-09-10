using System;
using System.Collections.Generic;
using System.Text;

namespace RealDiceCommon.Models.Roll
{
    public class RollRequest
    {
        public static readonly RollRequest Real1D6 = new RollRequest { N = 1, Dice = Dice.RealD6 };

        public uint N { get; set; }
        public Dice Dice { get; set; }
    }

    public enum Dice
    {
        RealD6
    }
}

