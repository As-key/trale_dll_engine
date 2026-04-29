using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TraleDLLManager;

namespace ChatGPT_API
{
    public class TraleTranslatorDLL : InterfaceTraleDLL
    {
        private TraleClientWebApi traleClientWebApi;

        public string GetDllName()
        {
            return "OpenAI ChatGPT API for TRALE";
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

        private class ResponseData
        {
            [JsonPropertyName("choices")]
            public ChoiceData[] choices { get; set; }
        }

        private class ChoiceData
        {
            [JsonPropertyName("message")]
            public MessageData message { get; set; }
        }

        private class MessageData
        {
            [JsonPropertyName("content")]
            public string content { get; set; }
        }

        public TraleClientWebApi(IHttpClientFactory clientFactory)
        {
            if (clientFactory == null)
            {
                throw new ArgumentNullException(nameof(clientFactory), "ClientFactory cannot be null.");
            }
            _clientFactory = clientFactory;
        }

        private static Dictionary<string, string> languageTable = new Dictionary<string, string>
        {
            {"de", "German"},
            {"en", "English"},
            {"en-gb", "English"},
            {"en-us", "English"},
            {"es", "Spanish"},
            {"fr", "French"},
            {"fr-ca", "French"},
            {"it", "Italian"},
            {"ja", "Japanese"},
            {"ko", "Korean"},
            {"nl", "Dutch"},
            {"pt", "Portuguese"},
            {"pt-br", "Portuguese (Brazil)"},
            {"pt-pt", "Portuguese (Portugal)"},
            {"ru", "Russian"},
            {"zh", "Chinese"},
            {"zh-cn", "Chinese (Simplified)"},
            {"zh-tw", "Chinese (Traditional)"}
        };

        public string[] translator(string srs_lang,
                                   string source,
                                   string dst_lang,
                                   Dictionary<string, string> options)
        {
            string[] ret_data = new string[2] { "", "" };
            if (options.ContainsKey("Auth_key") == false)
            {
                ret_data[0] = "";
                ret_data[1] = "Auth_key not found.";
                return ret_data;
            }
            else if (options["Auth_key"] == "")
            {
                ret_data[0] = "";
                ret_data[1] = "Auth_key not set.";
                return ret_data;
            }
            if (options.ContainsKey("Model") == false)
            {
                ret_data[0] = "";
                ret_data[1] = "Model not found.";
                return ret_data;
            }
            else if (options["Model"] == "")
            {
                ret_data[0] = "";
                ret_data[1] = "Model not set.";
                return ret_data;
            }
            if (options.ContainsKey("Explanation") == false)
            {
                ret_data[0] = "";
                ret_data[1] = "Explanation not found.";
                return ret_data;
            }
            else if (options["Explanation"] == "")
            {
                ret_data[0] = "";
                ret_data[1] = "Explanation not set.";
                return ret_data;
            }

            string content_str;
            if (srs_lang == dst_lang)
            {
                //content_str = options["Explanation"] + " \"" + source + "\"";
                content_str = options["Explanation"] + " " + source;
            }
            else
            {
                content_str = "I will give you the " + languageTable[srs_lang] + ". You will translate it into " + languageTable[dst_lang] + "." + " " + source;
            }

            try
            {
                string endpoint = "https://api.openai.com/v1/chat/completions";
                var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options["Auth_key"]);
                string jsonContent = JsonSerializer.Serialize(new
                {
                    model = options["Model"],
                    messages = new[]
                    {
                        new { role = "user", content = content_str }
                    }
                });
                request.Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(jsonContent));

                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                HttpClient client = _clientFactory.CreateClient();
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
                    ResponseData responsedata = JsonSerializer.Deserialize<ResponseData>(responseBody, jsonoptions);
                    ret_data[1] = responsedata.choices[0].message.content;
                }
                else
                {
                    if (string.IsNullOrEmpty(response.ReasonPhrase))
                    {
                        ret_data[1] = "Some errors have occurred.";
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
            catch (Exception e)
            {
                ret_data[1] = $"Some errors have occurred: {e.Message}";
            }
            return ret_data;
        }
    }
}