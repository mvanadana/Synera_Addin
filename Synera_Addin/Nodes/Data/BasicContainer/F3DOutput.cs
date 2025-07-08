using Fusion360Translator.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using Synera.Core.Graph.Data;
using Synera.Core.Graph.Enums;
using Synera.Core.Implementation.Graph;
using Synera.Core.Implementation.Graph.Data.DataTypes;
using Synera.Core.Implementation.UI;
using Synera.DataTypes;
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
        string clientId = "BukNHwRiiA5ikyGJvw4A5pBWW8rVtfii4pfTWL4v26kFeWGG";
        string clientSecret = "A4mKonxQsLgQOk3JynNBebZZdeHQhj4R6eG5qvSi1jygCBkoYECEme6vCD3gSkSe";
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

            InputParameterManager.AddParameter<SyneraString>(
                new LocalizableString("Fusion File Path"),
                new LocalizableString("Path to the .f3d file to upload."),
                ParameterAccess.Item);

            OutputParameterManager.AddParameter<SyneraString>(
                new LocalizableString("Forge URN"),
                new LocalizableString("URN of uploaded file on Forge."),
                ParameterAccess.Item);
        }

        protected override void SolveInstance(IDataAccess dataAccess)
        {
            if (!dataAccess.GetData(FilePathInputIndex, out SyneraString fileInput) || string.IsNullOrWhiteSpace(fileInput?.Value))
            {
                AddError("Fusion file path is not provided.");
                return;
            }

            string filePath = fileInput.Value;

            try
            {
                // ⚠️ Your real credentials

                string bucketKey = "vandana-bucket-synera-01"; // must be lowercase, unique

                //var uploader = new ForgeUploader(clientId, clientSecret);
                //string token = uploader.AuthenticateAsync().GetAwaiter().GetResult();

                //if (string.IsNullOrEmpty(token))
                //{
                //    AddError("Forge authentication failed.");
                //    return;
                //}

                //bool bucketCreated = uploader.CreateBucketAsync(bucketKey).GetAwaiter().GetResult();
                //if (!bucketCreated)
                //{
                //    AddError("Bucket creation failed.");
                //    return;
                //}

                //string objectId = uploader.UploadFileAsync(bucketKey, filePath).GetAwaiter().GetResult();
                //if (string.IsNullOrEmpty(objectId))
                //{
                //    AddError("File upload failed.");
                //    return;
                //}
                var inputValues = new List<double>();
                for (int i = __inputVariablesStartIndex; i < InputParameters.Count; i++)
                {
                    if (dataAccess.GetData(i, out SyneraDouble val))
                    {
                        inputValues.Add(val);
                    }
                }

                var result = RunFusionAutomationAsync(filePath, inputValues, new Progress<double>()).GetAwaiter().GetResult();
                var variables = result.variables;
                var bodies = result.bodies;
                UpdateInputs(variables);
                dataAccess.SetListData(0, bodies);
            }
            catch (Exception ex)
            {
                AddError($"Upload failed: {ex.Message}");
            }
        }
        public async Task<(List<FusionFileUploadNode.Variable> variables, List<IBody> bodies)> RunFusionAutomationAsync(
           string filePath,
           List<double> values,
           IProgress<double> progress)
        {
            progress.Report(0.05);
            await InitializeAsync();
            string fileName = Path.GetFileName(filePath);
            string inputObjectId = await UploadFileToBucketAsync(filePath, fileName);
            string workItemId = await SubmitWorkItemAsync(inputObjectId, fileName);
            Dictionary<string, string> downloadUrls = await WaitForWorkItemToCompleteAsync(workItemId);

            string resultDir = Path.Combine(Path.GetTempPath(), "fusion_result");
            Directory.CreateDirectory(resultDir);

            string stlPath = Path.Combine(resultDir, "result.stl");
            string paramPath = Path.Combine(resultDir, "params.txt");

            await DownloadFileAsync(downloadUrls["result.stl"], stlPath);
            await DownloadFileAsync(downloadUrls["params.txt"], paramPath);

            var variables = ParseParameters(paramPath);
            //var bodies = ParseSTL(stlPath);
            var bodies = new List<IBody>();
            progress.Report(1.0);
            return (variables, bodies);
        }
        private List<FusionFileUploadNode.Variable> ParseParameters(string paramFilePath)
        {
            var result = new List<FusionFileUploadNode.Variable>();

            foreach (var line in File.ReadAllLines(paramFilePath))
            {
                var parts = line.Split('=');
                if (parts.Length == 2 && double.TryParse(parts[1], out double val))
                {
                    result.Add(new FusionFileUploadNode.Variable
                    {
                        Name = parts[0],
                        Value = val
                    });
                }
            }

            return result;
        }

        private async Task DownloadFileAsync(string url, string destinationPath)
        {
            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            await using var fs = new FileStream(destinationPath, FileMode.Create);
            await response.Content.CopyToAsync(fs);
        }

        private async Task<Dictionary<string, string>> WaitForWorkItemToCompleteAsync(string workItemId)
        {
            var client = new RestClient($"https://developer.api.autodesk.com/da/{__region}/v3/workitems/{workItemId}");
            var request = new RestRequest
            {
                Method = Method.Get
            };


            request.AddHeader("Authorization", $"Bearer {_accessToken}");

            while (true)
            {
                var response = await client.ExecuteAsync(request);
                dynamic result = JsonConvert.DeserializeObject(response.Content);
                string status = result.status;

                if (status == "success")
                {
                    return new Dictionary<string, string>
                    {
                        ["result.stl"] = result.arguments["result.stl"].url,
                        ["params.txt"] = result.arguments["params.txt"].url
                    };
                }

                if (status == "failed")
                {
                    throw new Exception("Fusion DA job failed.");
                }

                //Thread.Sleep(3000); // Polling delay
            }
        }

        private void UpdateInputs(IList<Variable> modelVariables)
        {
            Document?.UndoRedoManager.OpenTransaction();
            try
            {
                _nodeVariables.Clear();
                var oldVars = InputParameters.Skip(__inputVariablesStartIndex).Select(p => p.Name.Value).ToList();
                var newVars = modelVariables.Select(v => v.Name).ToList();

                foreach (var name in oldVars.Except(newVars))
                {
                    RemoveRuntimeParameter(InputParameters.First(p => p.Name == name));
                }

                foreach (var name in newVars.Except(oldVars))
                {
                    var modelVar = modelVariables.First(v => v.Name == name);
                    var options = new InputParameterOptions(name, new LocalizableString("Parameter", typeof(Resources)), typeof(SyneraDouble))
                    {
                        DefaultValue = new DataTree<IGraphDataType>(new SyneraDouble(modelVar.Value))
                    };
                    AddRuntimeParameter(InputParameterManager.CreateParameter(options), InputParameters.Count);
                }

                foreach (var input in InputParameters.Skip(__inputVariablesStartIndex))
                {
                    var modelVar = modelVariables.First(v => v.Name == input.Name);
                    _nodeVariables.Add(modelVar);
                }
            }
            finally
            {
                Document?.UndoRedoManager.DiscardTransaction();
            }
        }
        public async Task CreateBucketAsync(string accessToken)
        {
            var url = "https://developer.api.autodesk.com/oss/v2/buckets";
            var client = new RestClient(url);
            // Fix for CS0117: 'Method' does not contain a definition for 'POST'
            // The issue arises because the `Method` enum does not have a member named `POST`.
            // Based on the provided type signature, the correct member name is `Post` (case-sensitive).

            var request = new RestRequest
            {
                Method = Method.Post
            };
            request.AddHeader("Authorization", $"Bearer {accessToken}");
            request.AddHeader("Content-Type", "application/json");

            var body = new
            {
                bucketKey = "synera-fusion-bucket",
                policyKey = "transient" // or "temporary" / "persistent"
            };

            request.AddJsonBody(body);

            var response = await client.ExecuteAsync(request);
            if (!response.IsSuccessful)
            {
                throw new Exception($"Bucket creation failed: {response.Content}");
            }
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
            var responseBody = await GetSignedUploadUrlAsync(_accessToken, __bucketKey, fileName);
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
            var uploadKey = ExtractUploadKey(responseBody);
            await UploadFileToSignedUrlAsync(urls.First(), filePath);
            await FinalizeSignedS3UploadAsync(_accessToken, __bucketKey, fileName, uploadKey);
           
            var urn = ConvertObjectIdToUrn();
            //byte[] fileBytes = File.ReadAllBytes(filePath);
            //var requestMessage = new HttpRequestMessage(HttpMethod.Put, url)
            //{
            //    Content = new ByteArrayContent(fileBytes)
            //};

            //requestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            //requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            //var response = await _httpClient.SendAsync(requestMessage);

            //if (!response.IsSuccessStatusCode)
            //{
            //    string error = await response.Content.ReadAsStringAsync();
            //    Console.WriteLine($"Error uploading file: {response.StatusCode} - {error}");
            //    return $"Failed: {response.StatusCode}";
            //}

            return $"Success: {response.StatusCode}";
        }
        public string ConvertObjectIdToUrn(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
                throw new ArgumentException("Invalid objectId");

            byte[] bytes = Encoding.UTF8.GetBytes(objectId);
            string urn = Convert.ToBase64String(bytes);
            return urn.TrimEnd('=').Replace('+', '-').Replace('/', '_'); // APS URNs are base64url-encoded
        }

        public string ExtractUploadKey(string responseBody)
        {
            var json = JObject.Parse(responseBody);
            var uploadKey = json["uploadKey"]?.ToString();

            if (string.IsNullOrEmpty(uploadKey))
                throw new Exception("uploadKey not found in signedS3Upload response.");

            return uploadKey;
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

        public async Task FinalizeSignedS3UploadAsync(string accessToken, string bucketKey, string objectKey, string uploadKey)
        {
            var url = $"https://developer.api.autodesk.com/oss/v2/buckets/{bucketKey}/objects/{objectKey}/signeds3upload";

            var payload = new
            {
                ossbucketKey = bucketKey,
                ossSourceFileObjectKey = objectKey,
                access = "full", // or "read"
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
                throw new Exception($"Finalize upload failed: {response.StatusCode} - {responseText}");
            }

            Console.WriteLine("Finalize upload successful!");
            Console.WriteLine(responseText);
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
        private async Task<string> SubmitWorkItemAsync(string objectId, string fileName)
        {
            var client = new RestClient($"https://developer.api.autodesk.com/da/{__region}/v3/workitems");
            var request = new RestRequest
            {
                Method = Method.Post
            };

            request.AddHeader("Authorization", $"Bearer {_accessToken}");
            request.AddHeader("Content-Type", "application/json");

            var body = new
            {
                activityId = __activityId,
                arguments = new Dictionary<string, object>
                {
                    ["inputFile"] = new
                    {
                        url = $"https://developer.api.autodesk.com/oss/v2/buckets/{__bucketKey}/objects/{fileName}",
                        headers = new Dictionary<string, string>
                        {
                            { "Authorization", $"Bearer {_accessToken}" }
                        }
                    },
                    ["result.stl"] = new { verb = "put" },
                    ["params.txt"] = new { verb = "put" }
                }
            };

            request.AddJsonBody(body);
            var response = await client.ExecuteAsync(request);
            dynamic result = JsonConvert.DeserializeObject(response.Content);
            return result.id;
        }

    }
}
