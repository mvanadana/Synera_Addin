using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;


namespace Synera_Addin
{
    
    public class ForgeAppBundleUploader
    {
        private readonly HttpClient _client;

        public ForgeAppBundleUploader()
        {
            _client = new HttpClient();
        }

        public async Task<string?> UploadAppBundleAsync(string accessToken, string zipFilePath)
        {
            string registerUrl = "https://developer.api.autodesk.com/da/us-east/v3/appbundles";

            var registerPayload = new
            {
                id = "ConfigureDesignAppBundle_v5",
                engine = "Autodesk.Fusion+Latest",
                description = "My first fusion appbundle based on the latest Fusion engine"
            };

            var requestContent = new StringContent(JsonSerializer.Serialize(registerPayload));
            requestContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            HttpResponseMessage registerResponse = await _client.PostAsync(registerUrl, requestContent);

            // If the appbundle already exists, register a new version
            if (registerResponse.StatusCode == HttpStatusCode.Conflict)
            {
                Console.WriteLine("AppBundle already exists. Registering a new version...");

                string versionUrl = "https://developer.api.autodesk.com/da/us-east/v3/appbundles/ConfigureDesignAppBundle/versions";
                registerResponse = await _client.PostAsync(versionUrl, requestContent);
            }

            if (!registerResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to register AppBundle: {registerResponse.StatusCode}");
                return null;
            }

            string responseString = await registerResponse.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(responseString);
            JsonElement root = doc.RootElement;

            var uploadParameters = root.GetProperty("uploadParameters");
            var endpointURL = uploadParameters.GetProperty("endpointURL").GetString();

            var formDataDict = new Dictionary<string, string>();
            foreach (var prop in uploadParameters.GetProperty("formData").EnumerateObject())
            {
                formDataDict[prop.Name] = prop.Value.GetString();
            }

            // Upload the zip file
            using var uploadClient = new HttpClient();
            using var multipartContent = new MultipartFormDataContent();
            foreach (var item in formDataDict)
            {
                multipartContent.Add(new StringContent(item.Value), item.Key);
            }

            var fileContent = new ByteArrayContent(File.ReadAllBytes(zipFilePath));
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipartContent.Add(fileContent, "file", Path.GetFileName(zipFilePath));

            var uploadResponse = await uploadClient.PostAsync(endpointURL, multipartContent);

            if (uploadResponse.StatusCode == HttpStatusCode.OK)
            {
                Console.WriteLine("AppBundle uploaded successfully.");
                return endpointURL;
            }
            else
            {
                Console.WriteLine($"Upload failed with status: {uploadResponse.StatusCode}");
                return null;
            }
        }
        public async Task<UploadAppBundleMetadata?> RegisterAppBundleAsync(string accessToken, string appBundleId, string zipFilePath)
        {
            string registerUrl = "https://developer.api.autodesk.com/da/us-east/v3/appbundles";

            var registerPayload = new
            {
                id = appBundleId,
                engine = "Autodesk.Fusion+Latest",
                description = "Fusion AppBundle using Design Automation"
            };

            var requestContent = new StringContent(JsonSerializer.Serialize(registerPayload));
            requestContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            HttpResponseMessage registerResponse = await _client.PostAsync(registerUrl, requestContent);

            // Fallback to version registration if AppBundle already exists
            if (registerResponse.StatusCode == HttpStatusCode.Conflict)
            {
                Console.WriteLine("⚠️ AppBundle already exists. Creating a new version...");
                string versionUrl = $"https://developer.api.autodesk.com/da/us-east/v3/appbundles/{appBundleId}/versions";
                registerResponse = await _client.PostAsync(versionUrl, requestContent);
            }

            if (!registerResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"❌ Failed to register AppBundle: {registerResponse.StatusCode}");
                return null;
            }

            string responseString = await registerResponse.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(responseString);
            JsonElement root = doc.RootElement;

            var uploadParams = root.GetProperty("uploadParameters");
            string endpointUrl = uploadParams.GetProperty("endpointURL").GetString() ?? "";

            var formDataDict = new Dictionary<string, string>();
            foreach (var prop in uploadParams.GetProperty("formData").EnumerateObject())
            {
                formDataDict[prop.Name] = prop.Value.GetString() ?? "";
            }

            string fullAppBundleId = root.GetProperty("id").GetString() ?? "";
            int version = root.GetProperty("version").GetInt32();

            return new UploadAppBundleMetadata
            {
                EndpointUrl = endpointUrl,
                FormData = formDataDict,
                AppBundleId = fullAppBundleId,
                Version = version
            };
        }
        public async Task<bool> UploadZipToS3Async(UploadAppBundleMetadata metadata, string zipFilePath)
        {
            if (!File.Exists(zipFilePath))
            {
                Console.WriteLine("❌ Zip file not found.");
                return false;
            }

            using var client = new HttpClient();
            using var content = new MultipartFormDataContent();

            foreach (var kvp in metadata.FormData)
            {
                content.Add(new StringContent(kvp.Value), kvp.Key);
            }

            var zipBytes = new ByteArrayContent(File.ReadAllBytes(zipFilePath));
            zipBytes.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(zipBytes, "file", Path.GetFileName(zipFilePath));

            var response = await client.PostAsync(metadata.EndpointUrl, content);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("✅ AppBundle uploaded to S3 successfully.");
                return true;
            }

            Console.WriteLine($"❌ Upload to S3 failed: {response.StatusCode}");
            return false;
        }
        public async Task<bool> CreateAppBundleAliasAsync(string accessToken, string appBundleId, string aliasId, int version)
        {
            string aliasUrl = $"https://developer.api.autodesk.com/da/us-east/v3/appbundles/{appBundleId}/aliases";

            var aliasPayload = new
            {
                id = aliasId,
                version = version
            };

            var content = new StringContent(JsonSerializer.Serialize(aliasPayload));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _client.PostAsync(aliasUrl, content);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("✅ Alias created successfully.");
                return true;
            }
            else
            {
                Console.WriteLine($"❌ Failed to create alias: {response.StatusCode}");
                return false;
            }
        }
        public async Task<bool> CreateActivityAsync(string accessToken, string activityId, string appBundleQualifiedId)
        {
            string url = "https://developer.api.autodesk.com/da/us-east/v3/activities";

            var payload = new
            {
                id = activityId,
                engine = "Autodesk.Fusion+Latest",
                commandLine = new[]
{
   @"$(engine.path)\Fusion360Core.exe --headless /Contents/main.ts"
},
            parameters = new Dictionary<string, object>
        {
            {
                "TaskParameters", new Dictionary<string, object>
                {
                    { "verb", "read" },
                    { "description", "the parameters for the script" },
                    { "required", false }
                }
            },
            {
                "PersonalAccessToken", new Dictionary<string, object>
                {
                    { "verb", "read" },
                    { "description", "the personal access token to use" },
                    { "required", true }
                }
            }
        },
                appbundles = new[]
                {
            appBundleQualifiedId  // e.g., Synera_NickName.ConfigureDesignAppBundle_v6+01
        },
                description = "Activity to run ConfigureDesign script"
            };

            var json = JsonSerializer.Serialize(payload);
            Console.WriteLine("📦 Payload Sent:\n" + json); // 👀 Debug print

            var content = new StringContent(json);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _client.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("✅ Activity created successfully.");
                return true;
            }
            else
            {
                Console.WriteLine($"❌ Failed to create activity: {response.StatusCode}");
                Console.WriteLine("📥 Error Details: " + responseBody);
                return false;
            }
        }

        public async Task<bool> CreateActivityAliasAsync(string accessToken, string activityId, int version, string aliasId)
        {
            string url = $"https://developer.api.autodesk.com/da/us-east/v3/activities/{activityId}/aliases";

            var aliasPayload = new
            {
                version = version,
                id = aliasId
            };

            var json = JsonSerializer.Serialize(aliasPayload);
            var content = new StringContent(json);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            HttpResponseMessage response = await _client.PostAsync(url, content);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"✅ Alias '{aliasId}' created for activity '{activityId}', version {version}.");
                return true;
            }
            else
            {
                Console.WriteLine($"❌ Failed to create activity alias: {response.StatusCode}");
                Console.WriteLine("📥 Error Details: " + responseBody);
                return false;
            }
        }
        public async Task<string?> CreateWorkItemAsync(
    string accessToken,
    string fullyQualifiedActivityId,
    string personalAccessToken,
    string fileUrn,
    Dictionary<string, string> parameters)
        {
            string url = "https://developer.api.autodesk.com/da/us-east/v3/workitems";

            // Construct inner object representing the TaskParameters argument
            var taskParametersObject = new
            {
                fileURN = fileUrn,
                parameters = parameters
            };

            // Serialize the taskParametersObject as a JSON string (for the WorkItem payload)
            string taskParametersJson = JsonSerializer.Serialize(taskParametersObject);

            var workItemPayload = new
            {
                activityId = fullyQualifiedActivityId,
                arguments = new Dictionary<string, object>
        {
            { "PersonalAccessToken", personalAccessToken },
            { "TaskParameters", taskParametersJson } // must be a stringified JSON
        }
            };

            string json = JsonSerializer.Serialize(workItemPayload);
            Console.WriteLine("📦 Payload Sent:\n" + json);

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _client.PostAsync(url, content);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("✅ WorkItem created successfully.");
                Console.WriteLine("🔁 Response: " + responseBody);

                using var doc = JsonDocument.Parse(responseBody);
                return doc.RootElement.GetProperty("id").GetString();
            }
            else
            {
                Console.WriteLine($"❌ Failed to create WorkItem: {response.StatusCode}");
                Console.WriteLine("📥 Error Details: " + responseBody);
                return null;
            }
        }
        public async Task<(string status, string? reportUrl)> CheckWorkItemStatusUntilCompleteAsync(
    string accessToken,
    string workItemId,
    int pollIntervalSeconds = 5,
    int timeoutMinutes = 10)
        {
            string url = $"https://developer.api.autodesk.com/da/us-east/v3/workitems/{workItemId}";
            var timeoutAt = DateTime.UtcNow.AddMinutes(timeoutMinutes);

            while (true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _client.SendAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ Failed to check WorkItem status: {response.StatusCode}");
                    Console.WriteLine("📥 Error Response: " + responseBody);
                    throw new HttpRequestException("Failed to check WorkItem status.");
                }

                using var doc = JsonDocument.Parse(responseBody);
                string status = doc.RootElement.GetProperty("status").GetString() ?? "unknown";
                string? reportUrl = doc.RootElement.TryGetProperty("reportUrl", out var reportProp)
                                    ? reportProp.GetString()
                                    : null;

                Console.WriteLine($"⏱️ Current Status: {status}");

                // If the work item has finished, return the result
                if (status is "success" or "failed" or "cancelled")
                {
                    Console.WriteLine($"✅ Final Status: {status}");
                    if (!string.IsNullOrEmpty(reportUrl))
                        Console.WriteLine($"📄 Report URL: {reportUrl}");
                    return (status, reportUrl);
                }

                // If we’ve exceeded timeout, abort
                if (DateTime.UtcNow > timeoutAt)
                {
                    Console.WriteLine("⏰ Timeout while waiting for WorkItem to complete.");
                    return ("timeout", null);
                }

                // Wait before polling again
                await Task.Delay(pollIntervalSeconds * 1000);
            }
        }

        private class UploadParameters
        {
            public string endpointURL { get; set; }
            public Dictionary<string, string> formData { get; set; }
        }

        public class UploadAppBundleMetadata
        {
            public string EndpointUrl { get; set; } = "";
            public Dictionary<string, string> FormData { get; set; } = new();
            public string AppBundleId { get; set; } = "";
            public int Version { get; set; }
        }

    }

}
