using Microsoft.Bot.Schema;
using RealDiceCommon.Models.Roll;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RealDiceBot.Services
{
    // XXX RollでなくてRollRequestかな。
    public interface IRollService
    {
        Task RequestAsync(IActivity referenceActivity, RollRequest rollRequest);
    }
}
