using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RealDiceBot.Models
{
    // https://docs.microsoft.com/ja-jp/azure/bot-service/bot-builder-howto-add-media-attachments?view=azure-bot-service-4.0&tabs=csharp

    public class StaticAssets
    {
        public StaticAssets(IList<StaticAssetFile> files)
        {
            Files = files.ToDictionary(x => x.Name);
        }
        public IDictionary<string, StaticAssetFile> Files { get; }
    }

    public class StaticAssetFile
    {
        public string Name { get { return System.IO.Path.GetFileName(Path); } }

        public string Url { get; set; }
        public string Path { get; set; }
        public string ContentType { get; set; }
    }
}
