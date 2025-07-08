using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

public class FinalizeUploadResult
{
    public string ObjectId { get; set; }
    public string BucketKey { get; set; }
    public string ObjectKey { get; set; }
    public string Location { get; set; }
    public long Size { get; set; }
    public string ContentType { get; set; }
    public string PolicyKey { get; set; }
}


