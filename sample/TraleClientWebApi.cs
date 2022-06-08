using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TraleDLLManager;

namespace DeepL_API_Free
{
    public class TraleTranslatorDLL : InterfaceTraleDLL
    {
        private TraleClientWebApi traleClientWebApi;

        public string GetDllName()
        {
            return "DeepL API Free for TRALE";
        }

        public TraleTranslatorDLL()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddHttpClient();
            services.AddSingleton<TraleClientWebApi>();
            var provider = services.BuildServiceProvider();
            traleClientWebApi = provider.GetRequiredService<TraleClientWebApi>();
        }

        public string[] doTranslate(string srs_lang,
                                    string source,
                                    string dst_lang,
                                    Dictionary<string, string> options)
        {
            return traleClientWebApi.translator(srs_lang, source, dst_lang, options);
        }
    }

    public class TraleClientWebApi
    {
        private static IHttpClientFactory _clientFactory;

        private class Responsedata
        {
            [JsonPropertyName("translations")]
            public Translatedata[] translations { get; set; }
        }

        private class Translatedata
        {
            [JsonPropertyName("detected_source_language")]
            public string detected_source_language { get; set; }

            [JsonPropertyName("text")]
            public string text { get; set; }
        }

        public TraleClientWebApi(IHttpClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
        }

        public string[] translator(string srs_lang,
                                   string source,
                                   string dst_lang,
                                   Dictionary<string, string> options)
        {
            string[] ret_data = new string[2] { "", "" };
            if (options.ContainsKey("auth_key") == false)
            {
                ret_data[0] = "";
                ret_data[1] = "auth_key not found.";
                return ret_data;
            }
            else if (options["auth_key"] == "")
            {
                ret_data[0] = "";
                ret_data[1] = "auth_key not set.";
                return ret_data;
            }
            try
            {
                //var request = new HttpRequestMessage(new HttpMethod("POST"), "https://api.deepl.com/v2/translate");
                var request = new HttpRequestMessage(new HttpMethod("POST"), "https://api-free.deepl.com/v2/translate");
                var contentList = new List<string>();
                contentList.Add("auth_key=" + options["auth_key"]);
                contentList.Add("source_lang=" + srs_lang);
                contentList.Add("text=" + source);
                contentList.Add("target_lang=" + dst_lang);
                request.Content = new StringContent(string.Join("&", contentList));
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");
                HttpClient client = _clientFactory.CreateClient("DeepL_API_Free");
                client.Timeout = TimeSpan.FromMilliseconds(30000);
                

                var response = client.SendAsync(request).Result;
                string responseBody = response.Content.ReadAsStringAsync().Result;
                ret_data[0] = response.StatusCode.ToString();
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    var jsonoptions = new JsonSerializerOptions
                    {
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };
                    Responsedata responsedata = JsonSerializer.Deserialize<Responsedata>(responseBody, jsonoptions);
                    ret_data[1] = responsedata.translations[0].text;
                }
                else
                {
                    if (string.IsNullOrEmpty(response.ReasonPhrase))
                    {
                        ret_data[1] = "Some errors has occurred.";
                    }
                    else
                    {
                        ret_data[1] = response.ReasonPhrase;
                    }
                }
            }
            catch (TaskCanceledException e)
            {
                ret_data[1] = "Request timeout.";
            }
            catch
            {
                ret_data[1] = "Some errors has occurred.";
            }
            return ret_data;
        }
    }
}