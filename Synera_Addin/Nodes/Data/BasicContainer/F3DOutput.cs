using Fusion360Translator.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using Synera.Core.Graph.Data;
using Synera.Core.Graph.Enums;
using Synera.Core.Implementation.Graph;
using Synera.Core.Implementation.Graph.Data.DataTypes;
using Synera.Core.Implementation.UI;
using Synera.Core.Modularity;
using Synera.DataTypes;
using Synera.DataTypes.Web;
using Synera.Kernels.Fem.Results;
using Synera.Kernels.Geometry;
using Synera.Localization;
using Synera.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Synera.Core.Implementation.ApplicationService.IO.BaseZipApplicationIO;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Path = System.IO.Path;

namespace Synera_Addin.Nodes.Data.BasicContainer
{
    [Guid("4dd60cf0-4797-4f93-a7aa-f51d4e126b9d")]
    public sealed class FusionFileUploadNode : Node
    {
        private const int FilePathInputIndex = 0;
        private const string __bucketKey = "aayush-08072025-joshi";
        private const string __activityId = "your-activity-id";
        private const string __region = "us-east";
        string clientId;
        string clientSecret;
        private string _accessToken;
        private const int __inputVariablesStartIndex = 3;
        private readonly List<Variable> _nodeVariables = new();
        // Add a private field for HttpClient to resolve the CS0103 error.
        private readonly HttpClient _httpClient = new HttpClient();
        public class Variable
        {
            public string Name { get; set; }

            public double Value { get; set; }
        }
        public async Task InitializeAsync()
        {
            _accessToken = await GetAccessToken(clientId, clientSecret);
        }
        public FusionFileUploadNode()
            : base(new LocalizableString("Fusion File Upload"))
        {
            Category = Categories.Data;
            Subcategory = Subcategories.Data.BasicContainer;
            Description = new LocalizableString("Uploads Fusion .f3d file to Autodesk Forge.");
            GuiPriority = 1;

            InputParameterManager.AddParameter<IAuthentication>(
                 new LocalizableString("Authentication"),
                 new LocalizableString("Output from Authentication Node"),
                 ParameterAccess.Item); 
            InputParameterManager.AddParameter<SyneraString>(
                new LocalizableString("Fusion File Path"),
                new LocalizableString("Path to the .f3d file to upload."),
                ParameterAccess.Item);

            OutputParameterManager.AddParameter<SyneraString>(
                new LocalizableString("Bodies/Parameters"),
                new LocalizableString("Bodies/properties of the uploaded file"),
                ParameterAccess.Item);
        }

        protected override void SolveInstance(IDataAccess dataAccess)
        {
            if (!dataAccess.GetData(0, out IAuthentication authObj) || authObj == null)
            {
                AddError("Missing authentication input. Connect the output of the Authentication node.");
                return;
            }
            if (!dataAccess.GetData(1, out SyneraString fileInput) || string.IsNullOrWhiteSpace(fileInput?.Value))
            {
                AddError("Fusion file path is not provided.");
                return;
            }
            
            dynamic authDynamic = authObj;
            clientId = authDynamic.AuthManager.Options.ClientId;
            clientSecret = authDynamic.AuthManager.Options.ClientSecret;
            string filePath = fileInput.Value;

            try
            {
                var inputValues = new List<double>();
                for (int i = __inputVariablesStartIndex; i < InputParameters.Count; i++)
                {
                    if (dataAccess.GetData(i, out SyneraDouble val))
                    {
                        inputValues.Add(val);
                    }
                }

                var result = RunFusionAutomationAsync(filePath, inputValues, new Progress<double>()).GetAwaiter().GetResult();
               
                dataAccess.SetData(0, result);
            }
            catch (Exception ex)
            {
                AddError($"Upload failed: {ex.Message}");
            }
        }
        public async Task<List<ObjectProperties>> RunFusionAutomationAsync(
           string filePath,
           List<double> values,
           IProgress<double> progress)
        {
            progress.Report(0.05);
            await InitializeAsync();
            string fileName = Path.GetFileName(filePath);
            string inputObjectId = await UploadFileToBucketAsync(filePath, fileName);
            string urn = inputObjectId;
            var fetcher = new ModelParameterFetcher(new HttpClient(), _accessToken);
            bool success = await TranslateToSvf2FormatAsync(_accessToken, urn);
            string status = await CheckTranslationStatusAsync(_accessToken, urn);
            Thread.Sleep(3000);
            string statusagain = await CheckTranslationStatusAsyncAgain(_accessToken, urn);
            var viewables = await GetViewablesAsync(_accessToken, urn);
            var hierarchy = await GetObjectHierarchyAsync(_accessToken, urn, viewables[0].Guid);
            var objectIds = ExtractAllObjectIds(hierarchy);
            var hierarchyFiltered = await GetFilteredObjectHierarchyAsync(_accessToken, urn, viewables[0].Guid, objectIds[0]);
            JObject propertiesJson = await GetAllObjectPropertiesAsync(_accessToken, urn, viewables[0].Guid);
            var ListOfViewable = ExtractObjectProperties(propertiesJson);

            return ListOfViewable;
        }

        

        public List<int> ExtractAllObjectIds(JObject json)
        {
            var objectIds = new List<int>();

            void Traverse(JToken node)
            {
                if (node == null)
                    return;

                if (node.Type == JTokenType.Object && node["objectid"] != null)
                {
                    if (int.TryParse(node["objectid"]?.ToString(), out int id))
                    {
                        objectIds.Add(id);
                    }
                }

                if (node["objects"] != null)
                {
                    foreach (var child in node["objects"])
                    {
                        Traverse(child);
                    }
                }
            }

            var rootObjects = json["data"]?["objects"];
            if (rootObjects != null)
            {
                foreach (var obj in rootObjects)
                {
                    Traverse(obj);
                }
            }

            return objectIds;
        }

        public async Task<JObject> QueryPropertiesAsync(
      string accessToken,
      string urn,
      string guid,
      string namePrefix = "",
      int offset = 0,
      int limit = 10)
        {
            string url = $"https://developer.api.autodesk.com/modelderivative/v2/designdata/{urn}/metadata/{guid}/properties:query";

            // Create the base payload
            var payload = new JObject
            {
                ["fields"] = new JArray("objectid", "name", "externalId", "properties.Dimensions"),
                ["pagination"] = new JObject
                {
                    ["offset"] = offset,
                    ["limit"] = limit
                },
                ["payload"] = "text"
            };

            // ONLY include query if namePrefix is provided
            if (!string.IsNullOrEmpty(namePrefix))
            {
                payload["query"] = new JObject
                {
                    ["$prefix"] = new JArray("name", namePrefix)
                };
            }

            var jsonContent = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = jsonContent;

            using var response = await _httpClient.SendAsync(request);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Query failed: {response.StatusCode} - {responseContent}");
            }

            return JObject.Parse(responseContent);
        }

        public async Task<JObject> GetAllObjectPropertiesAsync(string accessToken, string urn, string guid)
        {
            string url = $"https://developer.api.autodesk.com/modelderivative/v2/designdata/{urn}/metadata/{guid}/properties";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"❌ Failed to retrieve object properties: {response.StatusCode} - {responseContent}");
            }

            var json = JObject.Parse(responseContent);

            if (json["result"]?.ToString() == "success")
            {
                // This means the job is still processing
                throw new Exception("⚠️ Object properties are still being processed. Please retry after some time.");
            }

            return json;
        }
        public async Task<List<(string Name, string Role, string Guid)>> GetViewablesAsync(string accessToken, string urn)
        {
            string url = $"https://developer.api.autodesk.com/modelderivative/v2/designdata/{urn}/metadata";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to get metadata: {response.StatusCode} - {json}");
            }

            var result = new List<(string Name, string Role, string Guid)>();

            var jObject = JObject.Parse(json);
            var metadata = jObject["data"]?["metadata"];

            if (metadata != null)
            {
                foreach (var item in metadata)
                {
                    string guid = item["guid"]?.ToString();
                    string name = item["name"]?.ToString();
                    string role = item["role"]?.ToString();

                    if (!string.IsNullOrEmpty(guid))
                    {
                        result.Add((name, role, guid));
                    }
                }
            }

            return result;
        }

        public async Task<JObject> GetObjectHierarchyAsync(string accessToken, string urn, string guid)
        {
            int maxAttempts = 10;
            int delaySeconds = 5;
            string url = $"https://developer.api.autodesk.com/modelderivative/v2/designdata/{urn}/metadata/{guid}";

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using var response = await _httpClient.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to retrieve object hierarchy: {response.StatusCode} - {responseContent}");
                }

                var json = JObject.Parse(responseContent);

                if (json["data"] != null)
                {
                    Console.WriteLine($"✅ Object hierarchy loaded on attempt {attempt}.");
                    return json;
                }

                Console.WriteLine($"⏳ Attempt {attempt}: Still processing... Retrying in {delaySeconds} seconds.");
                await Task.Delay(delaySeconds * 1000);
            }

            throw new Exception("Object hierarchy is still being processed after maximum attempts. Try again later.");
        }

        public async Task<JObject> GetFilteredObjectHierarchyAsync(string accessToken, string urn, string guid, int objectId)
        {
            string url = $"https://developer.api.autodesk.com/modelderivative/v2/designdata/{urn}/metadata/{guid}?objectid={objectId}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"❌ Failed to retrieve filtered object hierarchy: {response.StatusCode} - {responseContent}");
            }

            var json = JObject.Parse(responseContent);

            if (json["data"] == null)
            {
                throw new Exception("⚠️ Filtered object hierarchy is still being processed or returned empty.");
            }

            Console.WriteLine("✅ Filtered object hierarchy retrieved successfully.");
            return json;
        }


        public async Task<bool> TranslateToSvf2FormatAsync(string accessToken, string urn)
        {
            string url = "https://developer.api.autodesk.com/modelderivative/v2/designdata/job";

            var payload = new
            {
                input = new
                {
                    urn = urn
                },
                output = new
                {
                    formats = new[]
                    {
                new
                {
                    type = "svf2",
                    views = new[] { "2d", "3d" }
                }
            }
                }
            };

            var jsonContent = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("x-ads-force", "true"); // Force re-translation
            request.Content = jsonContent;

            using var httpClient = new HttpClient();
            var response = await httpClient.SendAsync(request);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"❌ Model translation failed: {response.StatusCode} - {responseContent}");
            }

            Console.WriteLine("✅ SVF2 translation job submitted successfully.");
            return true;
        }

        public async Task<string> CheckTranslationStatusAsync(string accessToken, string urn)
        {
            string url = $"https://developer.api.autodesk.com/modelderivative/v2/designdata/{urn}/manifest";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var httpClient = new HttpClient();
            var response = await httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"❌ Failed to fetch manifest: {response.StatusCode} - {json}");
            }

            var manifest = JObject.Parse(json);
            var status = manifest["status"]?.ToString();

            if (string.IsNullOrEmpty(status))
                throw new Exception("❌ Translation status not found in manifest response.");

            Console.WriteLine($"🛈 Translation job status: {status}");

            return status;
        }

        public async Task<string> CheckTranslationStatusAsyncAgain(string accessToken, string urn)
        {
            string url = $"https://developer.api.autodesk.com/modelderivative/v2/designdata/{urn}/manifest";
            int maxAttempts = 20;
            int delaySeconds = 10;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to get manifest: {response.StatusCode} - {content}");
                }

                var json = JObject.Parse(content);
                string status = json["status"]?.ToString();
                string progress = json["progress"]?.ToString();

                Console.WriteLine($"⏳ Attempt {attempt + 1}: Status = {status}, Progress = {progress}");

                if (status == "success")
                {
                    Console.WriteLine("✅ Translation succeeded!");
                    return "success";
                }
                else if (status == "failed" || status == "timeout")
                {
                    Console.WriteLine($"❌ Translation {status}.");
                    return status;
                }

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }

            throw new TimeoutException("⏰ Translation polling timed out.");
        }
        public async Task<string> GetViewableGuidAsync(string urn, string accessToken)
        {
            string url = $"https://developer.api.autodesk.com/modelderivative/v2/designdata/{urn}/metadata";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            var data = JObject.Parse(json);
            var guid = data["data"]?["metadata"]?.First()?["guid"]?.ToString();

            if (string.IsNullOrEmpty(guid))
                throw new Exception("No GUID found in metadata response.");

            return guid;
        }
        public async Task<string> GetObjectTreeAsync(string urn, string viewableGuid, string accessToken)
        {
            string url = $"https://developer.api.autodesk.com/modelderivative/v2/designdata/{urn}/metadata/{viewableGuid}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Failed to get object tree: {response.StatusCode} - {json}");

            return json;
        }

        public async Task<string> GetAllPropertiesAsync(string urn, string viewableGuid, string accessToken)
        {
            string url = $"https://developer.api.autodesk.com/modelderivative/v2/designdata/{urn}/metadata/{viewableGuid}/properties";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Failed to get properties: {response.StatusCode} - {json}");

            return json;
        }
        public async Task<string> QueryFilteredPropertiesAsync(string urn, string viewableGuid, string accessToken)
        {
            string url = $"https://developer.api.autodesk.com/modelderivative/v2/designdata/{urn}/metadata/{viewableGuid}/properties:query";

            var payload = new
            {
                query = new Dictionary<string, object>
        {
            { "$prefix", new[] { "name", "M_Pile-Steel" } }
        },
                fields = new[]
                {
            "objectid",
            "name",
            "externalId",
            "properties.Dimensions"
        },
                pagination = new
                {
                    offset = 0,
                    limit = 10
                },
                payload = "text"
            };

            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = content;

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Failed to query specific properties: {response.StatusCode} - {json}");

            return json;
        }

        public async Task<string> GetSignedUploadUrlAsync(string accessToken, string bucketKey, string objectKey, int minutesExpiration = 2, string uploadKey = null)
        {
            // Base URL
            string url = $"https://developer.api.autodesk.com/oss/v2/buckets/{bucketKey}/objects/{objectKey}/signeds3upload";

            // Add optional parameters
            var query = $"?minutesExpiration={minutesExpiration}";
            if (!string.IsNullOrEmpty(uploadKey))
            {
                query += $"&uploadKey={uploadKey}";
            }

            var request = new HttpRequestMessage(HttpMethod.Get, url + query);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Error getting signed URL: {response.StatusCode} - {responseBody}");
            }

            // Extract the "urls" array from the JSON
            var json = JObject.Parse(responseBody);
            var urlsArray = json["urls"] as JArray;

            if (urlsArray == null)
                throw new Exception("No 'urls' found in response");

            var urls = new List<string>();
            foreach (var urlItem in urlsArray)
            {
                urls.Add(urlItem.ToString());
            }

            return responseBody;
        }
        private async Task<string> UploadFileToBucketAsync(string filePath, string fileName)
        {
            // Step 1: Get signed upload URL
            var responseBody = await GetSignedUploadUrlAsync(_accessToken, __bucketKey, fileName);

            var json = JObject.Parse(responseBody);
            var urlsArray = json["urls"] as JArray;

            if (urlsArray == null || !urlsArray.Any())
                throw new Exception("No 'urls' found in response");

            string signedUrl = urlsArray.First().ToString();
            string uploadKey = json["uploadKey"]?.ToString();

            if (string.IsNullOrEmpty(uploadKey))
                throw new Exception("uploadKey missing in signed URL response.");

            // Step 2: Upload file to S3 via signed URL
            await UploadFileToSignedUrlAsync(signedUrl, filePath);

            // Step 3: Finalize the upload
            var finalizeResult = await FinalizeSignedS3UploadAsync(_accessToken, __bucketKey, fileName, uploadKey);

            // Step 4: Convert objectId to URN
            string urn = ConvertObjectIdToUrn(finalizeResult.ObjectId);

            return urn;
        }

        public string ConvertObjectIdToUrn(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
                throw new ArgumentException("Invalid objectId");

            byte[] bytes = Encoding.UTF8.GetBytes(objectId);
            string urn = Convert.ToBase64String(bytes);
            return urn.TrimEnd('=').Replace('+', '-').Replace('/', '_'); // APS URNs are base64url-encoded
        }

        public async Task UploadFileToSignedUrlAsync(string signedUrl, string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found: " + filePath);

            byte[] fileBytes = File.ReadAllBytes(filePath);

            var request = new HttpRequestMessage(HttpMethod.Put, signedUrl)
            {
                Content = new ByteArrayContent(fileBytes)
            };

            // Optional but recommended
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

            try
            {
                using var httpClient = new HttpClient();
                var response = await httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Upload failed: {response.StatusCode} - {error}");
                }

                Console.WriteLine("Upload successful!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception during upload:");
                Console.WriteLine(ex.Message);
                throw;
            }
        }

        public async Task<FinalizeUploadResult> FinalizeSignedS3UploadAsync(string accessToken, string bucketKey, string objectKey, string uploadKey)
        {
            var url = $"https://developer.api.autodesk.com/oss/v2/buckets/{bucketKey}/objects/{objectKey}/signeds3upload";

            var payload = new
            {
                ossbucketKey = bucketKey,
                ossSourceFileObjectKey = objectKey,
                access = "full",
                uploadKey = uploadKey
            };

            string jsonBody = JsonConvert.SerializeObject(payload);

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var httpClient = new HttpClient();
            var response = await httpClient.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"❌ Finalize upload failed: {response.StatusCode} - {responseText}");
            }

            Console.WriteLine("✅ Finalize upload successful.");
            var resultJson = JObject.Parse(responseText);

            return new FinalizeUploadResult
            {
                ObjectId = resultJson["objectId"]?.ToString(),
                BucketKey = resultJson["bucketKey"]?.ToString(),
                ObjectKey = resultJson["objectKey"]?.ToString(),
                Location = resultJson["location"]?.ToString(),
                Size = resultJson["size"]?.ToObject<long>() ?? 0,
                ContentType = resultJson["contentType"]?.ToString(),
                PolicyKey = resultJson["policyKey"]?.ToString()
            };
        }
        public static async Task<string> GetAccessToken(string clientId, string clientSecret)
        {
            var client = new HttpClient();

            string authString = $"{clientId}:{clientSecret}";
            string base64Auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(authString));

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64Auth);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var content = new FormUrlEncodedContent(new[]
            {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("scope", "code:all bucket:create bucket:read data:create data:write data:read")
        });

            HttpResponseMessage response = await client.PostAsync("https://developer.api.autodesk.com/authentication/v2/token", content);
            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to get token: {error}");
            }

            string json = await response.Content.ReadAsStringAsync();
            dynamic result = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
            return result.access_token;
        }
        public class ObjectProperties
        {
            public int ObjectId { get; set; }
            public string Name { get; set; }
            public string ExternalId { get; set; }
            public JObject Properties { get; set; }
        }

        public List<ObjectProperties> ExtractObjectProperties(JObject responseJson)
        {
            var resultList = new List<ObjectProperties>();

            var collection = responseJson["data"]?["collection"] as JArray;
            if (collection == null) return resultList;

            foreach (var item in collection)
            {
                var obj = new ObjectProperties
                {
                    ObjectId = item["objectid"]?.Value<int>() ?? 0,
                    Name = item["name"]?.ToString() ?? "",
                    ExternalId = item["externalId"]?.ToString() ?? "",
                    Properties = item["properties"] as JObject ?? new JObject()
                };

                resultList.Add(obj);
            }

            return resultList;
        }

    }
}
