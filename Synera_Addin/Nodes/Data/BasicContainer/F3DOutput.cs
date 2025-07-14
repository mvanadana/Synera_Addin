using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Synera.Core.Graph.Data;
using Synera.Core.Graph.Enums;
using Synera.Core.Implementation.Graph;
using Synera.Core.Implementation.UI;
using Synera.DataTypes;
using Synera.DataTypes.Web;
using Synera.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Path = System.IO.Path;

namespace Synera_Addin.Nodes.Data.BasicContainer
{
    public class ForgeTokenResponse
    {
        public string access_token { get; set; } = string.Empty;
        public int expires_in
        {
            get; set;
        }
    }

    [Guid("4dd60cf0-4797-4f93-a7aa-f51d4e126b9d")]
    public sealed class FusionFileUploadNode : Node
    {
        private const int FilePathInputIndex = 0;
        private const string __bucketKey = "aayush-08072025-joshi";
        private const string __nickName = "Synera_NickName";
        private const string __region = "us-east";
        private const int __inputVariablesStartIndex = 3;

        private string clientId;
        private string clientSecret;

        private ForgeTokenResponse _tokenInfo;
        private DateTime _tokenAcquiredAt;

        private readonly HttpClient _httpClient = new HttpClient();

        // Computed property to access current token
        private string _accessToken => _tokenInfo?.access_token;

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
                dataAccess.SetData(0, result.ToString());
            }
            catch (Exception ex)
            {
                AddError($"Upload failed: {ex.Message}");
            }
        }

        public async Task<JObject> RunFusionAutomationAsync(
            string filePath,
            List<double> values,
            IProgress<double> progress)
        {
            progress.Report(0.05);
            await InitializeAsync();
            bool nicknameSet = await SetForgeAppNicknameAsync(_accessToken, __nickName);

            if (!nicknameSet)
            {
                AddError("Could not set Forge App nickname. Try a different one or ensure no data exists.");
                return JObject.FromObject(new
                {
                    message = "Token initialized and file ready for upload."
                })
                ;
            }

            bool result = await EnsureAppBundleUploadedAsync(
            _accessToken,
            "ConfigureDesignNew111",
            @"D:\SYNERA\Synera_Addin\Synera_Addin\ConfigureDesign.zip"
            );

            if (!result)
            {
                Console.WriteLine("AppBundle setup failed.");
            }
            string fileName = Path.GetFileName(filePath);

            // Dummy result return for now
            return JObject.FromObject(new
            {
                message = "Token initialized and file ready for upload.",
                file = fileName
            });
        }

        public async Task InitializeAsync()
        {
            if (_tokenInfo == null || IsTokenExpired())
            {
                _tokenInfo = await GetAccessToken(clientId, clientSecret);
                _tokenAcquiredAt = DateTime.UtcNow;
                Console.WriteLine($"🔐 New access token acquired. Expires in {_tokenInfo.expires_in} seconds.");
            }
            else
            {
                Console.WriteLine("✅ Using cached access token.");
            }
        }

        private bool IsTokenExpired()
        {
            if (_tokenInfo == null)
                return true;

            double elapsedSeconds = (DateTime.UtcNow - _tokenAcquiredAt).TotalSeconds;
            return elapsedSeconds >= (_tokenInfo.expires_in - 60); // 1-minute buffer
        }

        public static async Task<ForgeTokenResponse> GetAccessToken(string clientId, string clientSecret)
        {
            using var client = new HttpClient();

            string authString = $"{clientId}:{clientSecret}";
            string base64Auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(authString));

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64Auth);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("scope", "code:all bucket:create bucket:read data:create data:write data:read")
            });

            HttpResponseMessage response = await client.PostAsync("https://developer.api.autodesk.com/authentication/v2/token", content);
            string responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"❌ Failed to get token: {response.StatusCode} - {responseJson}");
            }

            var token = JsonConvert.DeserializeObject<ForgeTokenResponse>(responseJson);
            if (token == null || string.IsNullOrEmpty(token.access_token))
            {
                throw new Exception("❌ Invalid token response.");
            }

            return token;
        }

        public async Task<bool> SetForgeAppNicknameAsync(string accessToken, string nickname)
        {
            string url = "https://developer.api.autodesk.com/da/us-east/v3/forgeapps/me";

            var payload = new
            {
                nickname = nickname
            };

            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Patch, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"✅ Nickname set successfully: {nickname}");
                return true;
            }
            else if ((int)response.StatusCode == 409)
            {
                Console.WriteLine($"⚠️ Nickname conflict. The nickname '{nickname}' is already in use.");
                return false;
            }
            else
            {
                Console.WriteLine($"❌ Failed to set nickname: {response.StatusCode} - {responseContent}");
                return false;
            }
        }

        public async Task<bool> EnsureAppBundleUploadedAsync(string accessToken, string appBundleId, string zipFilePath)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            dynamic uploadParams = null;
            string baseUrl = "https://developer.api.autodesk.com/da/us-east/v3";
            // Step 1: Check if AppBundle already exists
            var checkResponse = await client.GetAsync($"{baseUrl}/appbundles/{appBundleId}");
            if (checkResponse.IsSuccessStatusCode)
            {
                Console.WriteLine("ℹ️ AppBundle exists. Creating new version...");

                // Step 2: Create new version
                var versionResponse = await client.PostAsync(
                    $"{baseUrl}/appbundles/{appBundleId}/versions",
                    new StringContent("{}", Encoding.UTF8, "application/json")
                );
                var versionJson = await versionResponse.Content.ReadAsStringAsync();

                if (!versionResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ Failed to create AppBundle version: {versionResponse.StatusCode} - {versionJson}");
                    return false;
                }

                dynamic versionData = JsonConvert.DeserializeObject(versionJson);
                uploadParams = versionData.uploadParameters;
            }
            else if (checkResponse.StatusCode == HttpStatusCode.NotFound)
            {
                Console.WriteLine("ℹ️ AppBundle not found. Creating new AppBundle...");

                // Step 3: Register new AppBundle
                var registerPayload = new
                {
                    id = appBundleId,
                    engine = "Autodesk.Fusion+2602_01", // ✅ Specific engine
                    description = "Fusion AppBundle with automatic versioning"
                };

                var registerContent = new StringContent(JsonConvert.SerializeObject(registerPayload), Encoding.UTF8, "application/json");
                var registerResponse = await client.PostAsync($"{baseUrl}/appbundles", registerContent);
                var registerJson = await registerResponse.Content.ReadAsStringAsync();

                if (!registerResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ Failed to register AppBundle: {registerResponse.StatusCode} - {registerJson}");
                    return false;
                }

                dynamic registerResult = JsonConvert.DeserializeObject(registerJson);
                uploadParams = registerResult.uploadParameters;
            }
            else
            {
                string err = await checkResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"❌ Unexpected error checking AppBundle: {checkResponse.StatusCode} - {err}");
                return false;
            }

            // Step 4: Upload ZIP to S3 using signed URL
            using var uploadClient = new HttpClient();
            using var form = new MultipartFormDataContent();

            var endpointUrl = (string)uploadParams.endpointURL;
            var formData = uploadParams.formData;

            foreach (JProperty field in formData)
            {
                if (field.Name != "file")
                {
                    form.Add(new StringContent(field.Value.ToString()), field.Name);
                }
            }

            using var zipStream = File.OpenRead(zipFilePath);
            var fileContent = new StreamContent(zipStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "file", Path.GetFileName(zipFilePath));

            var s3Response = await uploadClient.PostAsync(endpointUrl, form);
            if (!s3Response.IsSuccessStatusCode)
            {
                string error = await s3Response.Content.ReadAsStringAsync();
                Console.WriteLine($"❌ Upload to S3 failed: {s3Response.StatusCode} - {error}");
                return false;
            }

            Console.WriteLine("✅ AppBundle uploaded successfully to S3.");
            return true;
        }



    }
}
