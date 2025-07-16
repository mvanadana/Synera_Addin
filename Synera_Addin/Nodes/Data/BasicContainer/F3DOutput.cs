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
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
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
                new LocalizableString("Fusion url"),
                new LocalizableString("URL of the .f3d file."),
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

            if (!dataAccess.GetData(1, out SyneraString UrlPath) || string.IsNullOrWhiteSpace(UrlPath?.Value))
            {
                AddError("Fusion file path is not provided.");
                return;
            }

            dynamic authDynamic = authObj;
            clientId = authDynamic.AuthManager.Options.ClientId;
            clientSecret = authDynamic.AuthManager.Options.ClientSecret;
            string url = UrlPath.Value;

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
               
               
                var result = RunFusionAutomationAsync(url, inputValues, new Progress<double>()).GetAwaiter().GetResult();
                dataAccess.SetData(0, result.ToString());
            }
            catch (Exception ex)
            {
                AddError($"Upload failed: {ex.Message}");
            }
        }

        public async Task<JObject> RunFusionAutomationAsync(
            string urnOfFile,
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

            var decodedURN = ExtractAndDecodeUrnFromUrl(urnOfFile);

            string accessToken = _accessToken;
            string appBundleId = "ConfigureDesignAppBundle_v60";
            string zipPath = @"E:\DT\Synera_Addin\Synera_Addin\ConfigureDesign.zip";
            string activityId = "ConfigureDesignActivity";
            string aliasId = "0156";
            string appBundleQualifiedId = __nickName + "."+appBundleId+"+"+ aliasId;
            string pAT = "7b0148b7e25f90282194409afb6ff8b339ca1829";
            string fullyQualifiedActivityId = __nickName + "." + activityId + "+" + aliasId;
            var parameters = new Dictionary<string, string>
{
    { "d3", "40mm" }
};
            var uploader = new ForgeAppBundleUploader();

            var metadata = await uploader.RegisterAppBundleAsync(accessToken, appBundleId, zipPath);

            if (metadata != null)
            {
                bool uploaded = await uploader.UploadZipToS3Async(metadata, zipPath);
                if (uploaded)
                {
                    await uploader.CreateAppBundleAliasAsync(accessToken, appBundleId, aliasId, metadata.Version);
                }
            }

            await uploader.CreateActivityAsync(accessToken, activityId, appBundleQualifiedId);

            await uploader.CreateActivityAliasAsync(accessToken, activityId, 1, aliasId+"mycurrentAlias");
            var workItemId = await uploader.CreateWorkItemAsync(accessToken, fullyQualifiedActivityId, pAT, decodedURN, parameters);

            

            // Dummy result return for now
            return JObject.FromObject(new
            {
                message = "Token initialized and file ready for upload."
                
            });
        }


        public static string ExtractAndDecodeUrnFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("Input URL cannot be null or empty.");

            var matches = Regex.Matches(url, @"(?<=/)([a-zA-Z0-9_-]{20,})(?=/|$)");

            foreach (Match match in matches)
            {
                string candidate = match.Value;
                try
                {
                    string decoded = DecodeBase64Urn(candidate);

                    if (decoded.StartsWith("urn:adsk"))
                        return decoded;
                }
                catch
                {
                    // Ignore
                }
            }

            throw new InvalidOperationException("No valid URN found in the URL.");
        }

        public static string DecodeBase64Urn(string base64)
        {
            // Add padding if needed
            int padding = 4 - (base64.Length % 4);
            if (padding != 4)
            {
                base64 = base64 + new string('=', padding);
            }

            base64 = base64.Replace('-', '+').Replace('_', '/');

            byte[] bytes = Convert.FromBase64String(base64);
            return Encoding.UTF8.GetString(bytes);
        }


        public static async Task<string> FollowRedirectAndGetFinalUrlAsync(string shortUrl)
        {
            using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }))
            {
                var response = await client.GetAsync(shortUrl);

                if (response.StatusCode == HttpStatusCode.Redirect ||
                    response.StatusCode == HttpStatusCode.MovedPermanently ||
                    response.StatusCode == HttpStatusCode.Found)
                {
                    return response.Headers.Location.ToString();
                }

                throw new Exception("The URL did not redirect.");
            }
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

        

    }
}
