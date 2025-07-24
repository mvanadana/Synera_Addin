using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Synera.Core;
using Synera.Core.Graph.Data;
using Synera.Core.Graph.Enums;
using Synera.Core.Implementation.ApplicationService;
using Synera.Core.Implementation.Graph;
using Synera.Core.Implementation.Graph.Data.DataTypes;
using Synera.Core.Implementation.UI;
using Synera.DataTypes;
using Synera.DataTypes.Web;
using Synera.Kernels.Geometry;
using Synera.Kernels.Translators;
using Synera.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
    public sealed class FusionRun : Node
    {
        private const int FilePathInputIndex = 0;
        private const string __bucketKey = "aayush-08072025-joshi";
        private const string __nickName = "Synera_NickName";
        private const string __region = "us-east";
        private const int __inputVariablesStartIndex = 2;

        private string clientId;
        private string clientSecret;

        private ForgeTokenResponse _tokenInfo;
        private DateTime _tokenAcquiredAt;

        private readonly HttpClient _httpClient = new HttpClient();
        public event EventHandler ParametersUpdated;

        private string _accessToken => _tokenInfo?.access_token;

        public class Variable
        {
            public string Name
            {
                get; set;
            }
            public double Value
            {
                get; set;
            }
        }

        private readonly List<Variable> _nodeVariables = new List<Variable>();
        private bool _ignoreSolutionExpired;

        [DataMember(Name = "UserParameters")]
        private Variable[] NodeVariablesSerialized
        {
            get => _nodeVariables.ToArray();
            set => UpdateInputs(value);
        }

        public FusionRun()
            : base(new LocalizableString("Run Fusion"))
        {
            Category = Categories.Data;
            Subcategory = Subcategories.Data.BasicContainer;
            Description = new LocalizableString("Update the user parameters of Fusion file.");
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
            var parameters = new Dictionary<string, string>();
            for (int i = __inputVariablesStartIndex; i < InputParameters.Count; i++)
            {
                var param = InputParameters[i];
                if (dataAccess.GetData(i, out SyneraDouble val))
                {
                    parameters[param.Name.Value] = val.Value.ToString();
                }
            }
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


                var result = RunFusionAutomationAsync(url, parameters, inputValues, new Progress<double>()).GetAwaiter().GetResult();
                dataAccess.SetData(0, result.ToString());
            }
            catch (Exception ex)
            {
                AddError($"Upload failed: {ex.Message}");
            }
        }
        public override void ExpireSolution()
        {
            if (!_ignoreSolutionExpired)
                base.ExpireSolution();
        }
        private void UpdateInputs(IList<Variable> modelVariables)
        {
            Document?.UndoRedoManager.OpenTransaction();
            try
            {
                _ignoreSolutionExpired = true;
                _nodeVariables.Clear();

                var oldVariables = InputParameters.Skip(__inputVariablesStartIndex).Select(c => c.Name.Value).ToList();
                var newVariables = modelVariables.Select(c => c.Name).ToList();

                var toDelete = oldVariables.Except(newVariables).ToList();
                var toAdd = newVariables.Except(oldVariables).ToList();
                var toUpdate = oldVariables.Intersect(newVariables).ToList();

                foreach (var name in toDelete)
                {
                    var input = InputParameters.Skip(__inputVariablesStartIndex).FirstOrDefault(p => p.Name == name);
                    if (input != null)
                        RemoveRuntimeParameter(input);
                }

                // ADD new parameters
                foreach (var name in toAdd)
                {
                    var variable = modelVariables.First(p => p.Name == name);
                    var options = new InputParameterOptions(name, new LocalizableString($"Input: {name}"), typeof(SyneraDouble))
                    {
                        DefaultValue = new DataTree<IGraphDataType>(new SyneraDouble(variable.Value)),
                        HasDynamicDefaultData = true
                    };

                    var param = InputParameterManager.CreateParameter(options);
                    AddRuntimeParameter(param, InputParameters.Count);

                    param.DefaultGraphData = new DataTree<IGraphDataType>(new SyneraDouble(variable.Value));

                    param.CollectData();
                }

                // UPDATE existing values
                foreach (var name in toUpdate)
                {
                    var input = InputParameters.Skip(__inputVariablesStartIndex).FirstOrDefault(p => p.Name == name);
                    var variable = modelVariables.First(p => p.Name == name);

                    var defaultVal = ((SyneraDouble)input.DefaultGraphData.GetAllData().First()).Value;

                    if (!SyneraMath.EpsilonEquals(defaultVal, variable.Value))
                    {
                        input.DefaultGraphData = new DataTree<IGraphDataType>(new SyneraDouble(variable.Value));
                    }
                }

                // Sync internal cache
                foreach (var variable in modelVariables)
                {
                    _nodeVariables.Add(variable);
                }
                ParametersUpdated?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                Document?.UndoRedoManager.DiscardTransaction();
                _ignoreSolutionExpired = false;
            }
        }

        public async Task<JObject> RunFusionAutomationAsync(string urnOfFile, Dictionary<string, string> parameters,
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

            var decodedURNlist = ExtractAndDecodeUrnFromUrl(urnOfFile);
            var decodedURN = decodedURNlist[1];

            string aliasId = "0305";
            string accessToken = _accessToken;
            string activityId = "ConfigureDesignActivity_305";
            string appBundleId = "ConfigureDesignAppBundle_v305";
            string pAT = "bf738a19c5667dbffa7fad82f68cabea59a025ff";
            string zipPath = @"E:\DT\Synera_Addin\Synera_Addin\ConfigureDesign.zip";

            string appBundleQualifiedId = __nickName + "." + appBundleId + "+" + aliasId;
            string fullyQualifiedActivityId = __nickName + "." + activityId + "+" + aliasId + "mycurrentAlias";

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

            await uploader.CreateActivityAliasAsync(accessToken, activityId, 1, aliasId + "mycurrentAlias");
            var WorkItemStepResult = await uploader.CreateWorkItemAsyncForStep(accessToken, fullyQualifiedActivityId, pAT, decodedURN, parameters);
            var result = await uploader.CheckWorkItemStatusAsync(accessToken, WorkItemStepResult.WorkItemId);
            var filepath = @"C:\Users\Vandana Mishra\Desktop\output";
            var isDownloaded  = await uploader.DownloadStepFileAsync( WorkItemStepResult.OutputStepUrl, filepath);
            while (result.status == "inprogress")
            {
                result = await uploader.CheckWorkItemStatusAsync(accessToken, WorkItemStepResult.WorkItemId);
            }

            Console.WriteLine("Status: " + result.status);
            if (!string.IsNullOrEmpty(result.reportUrl))
            {
                Console.WriteLine("Download Report: " + result.reportUrl);
                var userParameter = await uploader.FetchOutputJsonFromReportAsync(result.reportUrl);
                var modelVariables = new List<Variable>();
                foreach (var pair in userParameter)  // Assuming JObject
                {
                    var match = Regex.Match(pair.Value, @"[-+]?[0-9]*\.?[0-9]+");
                    if (match.Success && double.TryParse(match.Value, out double val))
                    {
                        modelVariables.Add(new Variable { Name = pair.Key, Value = val });
                    }
                }

                var dispatcher = Synera.Wpf.Common.WindowsManager.MainWindow?.Dispatcher;
                if (dispatcher == null)
                {
                    UpdateInputs(modelVariables);
                }
                else
                {
                    dispatcher.Invoke(() => UpdateInputs(modelVariables));
                }
            }

            return JObject.FromObject(new
            {
                message = "Token initialized and file ready for upload."

            });
        }

        public static List<string> ExtractAndDecodeUrnFromUrl(string url)
        {
            var decodedUrns = new List<string>();

            // Find all base64-ish looking strings in the URL path
            var matches = Regex.Matches(url, @"dXJu[^/]+");

            foreach (Match match in matches)
            {
                try
                {
                    string base64 = match.Value;

                    // Decode base64-URL-safe string
                    string decoded = DecodeBase64LikeUrn(base64);

                    // Only include valid Autodesk URNs
                    if (decoded.StartsWith("urn:adsk"))
                    {
                        decodedUrns.Add(decoded);
                    }
                }
                catch
                {
                    // Ignore invalid base64 strings
                }
            }

            return decodedUrns;
        }

        private static string DecodeBase64LikeUrn(string encodedUrn)
        {
            // Autodesk Forge URNs are prefixed with dXJu (which is "urn" in base64)
            // Trim the prefix if needed
            string base64 = encodedUrn;

            // Replace URL-safe characters
            base64 = base64.Replace('-', '+').Replace('_', '/');

            // Add base64 padding if needed
            int padding = 4 - (base64.Length % 4);
            if (padding != 4)
            {
                base64 += new string('=', padding);
            }

            byte[] data = Convert.FromBase64String(base64);
            return Encoding.UTF8.GetString(data);
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
