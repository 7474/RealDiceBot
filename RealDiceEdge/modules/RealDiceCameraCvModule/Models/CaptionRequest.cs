using System;
using System.Collections.Generic;
using System.Text;

namespace RealDiceCameraCvModule.Models
{
    public class CaptionRequest
    {
        public string Caption { get; set; }
        public CognitiveResult CognitiveResult { get; set; }
    }
}
