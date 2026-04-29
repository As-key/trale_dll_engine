using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TraleDLLManager;

namespace LMStudio_TRALE
{
    public class TraleTranslatorDLL : InterfaceTraleDLL
    {
        private readonly TraleClientWebApi client = new TraleClientWebApi();

        public string GetDllName()
        {
            return "LM Studio TRALE TranslateGemma";
        }

        public string[] doTranslate(
            string srs_lang,
            string source,
            string dst_lang,
            Dictionary<string, string> options)
        {
            return client.Translate(srs_lang, source, dst_lang, options);
        }
    }

    public class TraleClientWebApi
    {
        private class ModelsResponse
        {
            [JsonPropertyName("data")]
            public ModelData[] data { get; set; }
        }

        private class ModelData
        {
            [JsonPropertyName("id")]
            public string id { get; set; }
        }

        private class CompletionResponse
        {
            [JsonPropertyName("choices")]
            public CompletionChoice[] choices { get; set; }

            [JsonPropertyName("error")]
            public ErrorData error { get; set; }
        }

        private class CompletionChoice
        {
            [JsonPropertyName("text")]
            public string text { get; set; }

            [JsonPropertyName("finish_reason")]
            public string finish_reason { get; set; }
        }

        private class ErrorData
        {
            [JsonPropertyName("message")]
            public string message { get; set; }
        }

        public string[] Translate(
            string srs_lang,
            string source,
            string dst_lang,
            Dictionary<string, string> options)
        {
            string[] ret = new string[2] { "", "" };

            if (string.IsNullOrWhiteSpace(source))
            {
                ret[0] = "OK";
                ret[1] = "";
                return ret;
            }

            string sourceLang = string.IsNullOrWhiteSpace(srs_lang) ? "" : srs_lang.Trim();
            string targetLang = string.IsNullOrWhiteSpace(dst_lang) ? "" : dst_lang.Trim();

            if (string.IsNullOrWhiteSpace(sourceLang))
            {
                ret[0] = "";
                ret[1] = "Source language is empty.";
                return ret;
            }

            if (string.IsNullOrWhiteSpace(targetLang))
            {
                ret[0] = "";
                ret[1] = "Target language is empty.";
                return ret;
            }

            string baseUrl = GetOption(options, "BaseUrl", "http://127.0.0.1:1234/v1").TrimEnd('/');
            string model = GetOption(options, "Model", "");
            bool autoUseFirstModel = ParseBool(GetOption(options, "AutoUseFirstModel", "true"), true);

            int timeoutSeconds = ParseInt(GetOption(options, "TimeoutSeconds", "180"), 180);
            int maxTokens = ParseInt(GetOption(options, "MaxTokens", "256"), 256);

            double temperature = ParseDouble(GetOption(options, "Temperature", "0"), 0.0);
            double topP = ParseDouble(GetOption(options, "TopP", "1"), 1.0);

            bool includeBos = ParseBool(GetOption(options, "IncludeBos", "true"), true);
            string bosToken = GetOption(options, "BosToken", "<bos>");

            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

                    if (string.IsNullOrWhiteSpace(model) && autoUseFirstModel)
                        model = GetFirstLoadedModel(http, baseUrl);

                    if (string.IsNullOrWhiteSpace(model))
                    {
                        ret[0] = "";
                        ret[1] = "LM Studio model is not specified or loaded.";
                        return ret;
                    }

                    string prompt = BuildTranslateGemmaPrompt(
                        sourceLang,
                        targetLang,
                        source,
                        includeBos,
                        bosToken);

                    var requestObj = new
                    {
                        model = model,
                        prompt = prompt,
                        stream = false,
                        temperature = temperature,
                        top_p = topP,
                        max_tokens = maxTokens,
                        stop = new[]
                        {
                            "<end_of_turn>",
                            "<eos>"
                        }
                    };

                    string requestJson = JsonSerializer.Serialize(requestObj, JsonOptions());

                    var request = new HttpRequestMessage(
                        HttpMethod.Post,
                        baseUrl + "/completions");

                    request.Content = new StringContent(
                        requestJson,
                        Encoding.UTF8,
                        "application/json");

                    var response = http.SendAsync(request).Result;
                    string responseBody = response.Content.ReadAsStringAsync().Result;

                    ret[0] = response.StatusCode.ToString();

                    if (!response.IsSuccessStatusCode)
                    {
                        ret[1] = ExtractError(responseBody, response.ReasonPhrase);
                        return ret;
                    }

                    var data = JsonSerializer.Deserialize<CompletionResponse>(
                        responseBody,
                        JsonOptions());

                    string translated = data?.choices != null && data.choices.Length > 0
                        ? data.choices[0].text
                        : "";

                    translated = CleanOutput(translated);

                    if (string.IsNullOrWhiteSpace(translated))
                    {
                        ret[0] = "";
                        ret[1] = "LM Studio returned empty translation.";
                        return ret;
                    }

                    ret[1] = translated;
                    return ret;
                }
            }
            catch (TaskCanceledException)
            {
                ret[0] = "";
                ret[1] = "Request timeout. Check LM Studio server and loaded model.";
                return ret;
            }
            catch (Exception e)
            {
                ret[0] = "";
                ret[1] = "LM Studio TRALE failed: " + e.Message;
                return ret;
            }
        }

        private static string BuildTranslateGemmaPrompt(
            string sourceLang,
            string targetLang,
            string sourceText,
            bool includeBos,
            string bosToken)
        {
            var sb = new StringBuilder();

            if (includeBos && !string.IsNullOrEmpty(bosToken))
                sb.Append(bosToken);

            sb.Append("<start_of_turn>user\n");

            sb.Append("You are a professional ");
            sb.Append(sourceLang);
            sb.Append(" to ");
            sb.Append(targetLang);
            sb.Append(" translator. Your goal is to accurately convey the meaning and nuances of the original ");
            sb.Append(sourceLang);
            sb.Append(" text while adhering to ");
            sb.Append(targetLang);
            sb.Append(" grammar, vocabulary, and cultural sensitivities.\n");

            sb.Append("Produce only the ");
            sb.Append(targetLang);
            sb.Append(" translation, without any additional explanations or commentary. Please translate the following ");
            sb.Append(sourceLang);
            sb.Append(" text into ");
            sb.Append(targetLang);
            sb.Append(":\n\n\n");

            sb.Append(sourceText.Trim());

            sb.Append("<end_of_turn>\n");
            sb.Append("<start_of_turn>model\n");

            return sb.ToString();
        }

        private static string GetFirstLoadedModel(HttpClient http, string baseUrl)
        {
            try
            {
                var response = http.GetAsync(baseUrl + "/models").Result;
                string body = response.Content.ReadAsStringAsync().Result;

                if (!response.IsSuccessStatusCode)
                    return "";

                var data = JsonSerializer.Deserialize<ModelsResponse>(body, JsonOptions());

                if (data?.data == null || data.data.Length == 0)
                    return "";

                return data.data[0].id ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string CleanOutput(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            text = text.Trim();

            text = text.Replace("<end_of_turn>", "");
            text = text.Replace("<eos>", "");
            text = text.Replace("<bos>", "");
            text = text.Replace("<start_of_turn>model", "");
            text = text.Replace("<start_of_turn>user", "");
            text = text.Replace("<start_of_turn>", "");

            text = Regex.Replace(
                text,
                @"<think>.*?</think>",
                "",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            return text.Trim();
        }

        private static string ExtractError(string json, string reasonPhrase)
        {
            try
            {
                var data = JsonSerializer.Deserialize<CompletionResponse>(json, JsonOptions());

                if (!string.IsNullOrWhiteSpace(data?.error?.message))
                    return data.error.message;
            }
            catch
            {
            }

            return string.IsNullOrWhiteSpace(reasonPhrase) ? json : reasonPhrase;
        }

        private static string GetOption(
            Dictionary<string, string> options,
            string key,
            string fallback)
        {
            if (options == null)
                return fallback;

            if (options.ContainsKey(key))
                return options[key];

            foreach (var pair in options)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                    return pair.Value;
            }

            return fallback;
        }

        private static int ParseInt(string text, int fallback)
        {
            int v;
            return int.TryParse(text, out v) ? v : fallback;
        }

        private static double ParseDouble(string text, double fallback)
        {
            double v;
            return double.TryParse(
                text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out v)
                ? v
                : fallback;
        }

        private static bool ParseBool(string text, bool fallback)
        {
            bool v;
            return bool.TryParse(text, out v) ? v : fallback;
        }

        private static JsonSerializerOptions JsonOptions()
        {
            return new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                PropertyNameCaseInsensitive = true
            };
        }
    }
}