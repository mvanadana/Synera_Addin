using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Synera_Addin.Nodes.Data.BasicContainer
{
    public class ModelParameterFetcher
    {
        private readonly HttpClient _httpClient;
        private readonly string _accessToken;

        public ModelParameterFetcher(HttpClient httpClient, string accessToken)
        {
            _httpClient = httpClient;
            _accessToken = accessToken;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        public async Task<string> GetMetadataGuidAsync(string urn)
        {
           
            string safeUrn = Uri.EscapeDataString(urn);

            string url = $"https://developer.api.autodesk.com/modelderivative/v2/designdata/{safeUrn}/metadata";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _httpClient.SendAsync(request);
            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to get metadata. {response.StatusCode}: {json}");
            }

            var data = JObject.Parse(json);
            var guid = data["data"]["metadata"]?.First()?["guid"]?.ToString();

            return guid;
        }

        public async Task<JObject> GetModelPropertiesAsync(string urn, string guid)
        {
            string url = $"https://developer.api.autodesk.com/modelderivative/v2/designdata/{urn}/metadata/{guid}/properties";
            var response = await _httpClient.GetAsync(url);
            string json = await response.Content.ReadAsStringAsync();

            return JObject.Parse(json);
        }
    }

}
