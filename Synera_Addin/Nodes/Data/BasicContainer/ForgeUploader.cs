// File: ForgeUploader.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Fusion360Translator.Services
{
    public class ForgeUploader
    {
        private readonly string _clientId;
        private readonly string _clientSecret;
        private string _accessToken;

        public ForgeUploader(string clientId, string clientSecret)
        {
            _clientId = clientId;
            _clientSecret = clientSecret;
        }

        public async Task<string> AuthenticateAsync()
        {
            using var client = new HttpClient();
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", _clientId),
                new KeyValuePair<string, string>("client_secret", _clientSecret),
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("scope", "data:read data:write data:create bucket:create bucket:read")
            });

            var response = await client.PostAsync("https://developer.api.autodesk.com/authentication/v1/authenticate", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            dynamic result = JsonConvert.DeserializeObject(json);
            _accessToken = result.access_token;
            return _accessToken;
        }

        public async Task<bool> CreateBucketAsync(string bucketKey)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var body = new
            {
                bucketKey = bucketKey.ToLower(),
                policyKey = "transient"
            };

            var jsonContent = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://developer.api.autodesk.com/oss/v2/buckets", jsonContent);

            // StatusCode 409 = bucket already exists
            return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Conflict;
        }

        public async Task<string> UploadFileAsync(string bucketKey, string filePath)
        {
            var objectName = Path.GetFileName(filePath);

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var fileBytes = await File.ReadAllBytesAsync(filePath);
            var content = new ByteArrayContent(fileBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var url = $"https://developer.api.autodesk.com/oss/v2/buckets/{bucketKey}/objects/{objectName}";
            var response = await client.PutAsync(url, content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            dynamic result = JsonConvert.DeserializeObject(json);
            return result.objectId;
        }
    }
}
