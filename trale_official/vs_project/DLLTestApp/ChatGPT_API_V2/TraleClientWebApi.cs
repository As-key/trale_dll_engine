using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TraleDLLManager;

namespace ChatGPT_API_FOR_BAKIN
{
    public class TraleTranslatorDLL : InterfaceTraleDLL
    {
        private TraleClientWebApi traleClientWebApi;

        public string GetDllName() => "OpenAI ChatGPT API for TRALE (BAKIN Localize)";

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

        // ==== OpenAI Chat Completions 用 (function calling) ====

        private class ChatRequest
        {
            [JsonPropertyName("model")] public string Model { get; set; }
            [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; }
            [JsonPropertyName("temperature")] public double Temperature { get; set; } = 0.0;
            [JsonPropertyName("top_p")] public double TopP { get; set; } = 1.0;
            [JsonPropertyName("tools")] public List<ToolSpec> Tools { get; set; }
            [JsonPropertyName("tool_choice")] public object ToolChoice { get; set; } // e.g. "required"
        }

        private class ChatMessage
        {
            [JsonPropertyName("role")] public string Role { get; set; }
            [JsonPropertyName("content")] public string Content { get; set; }
        }

        private class ToolSpec
        {
            [JsonPropertyName("type")] public string Type { get; set; } = "function";
            [JsonPropertyName("function")] public FunctionSpec Function { get; set; }
        }

        private class FunctionSpec
        {
            [JsonPropertyName("name")] public string Name { get; set; }
            [JsonPropertyName("description")] public string Description { get; set; }
            [JsonPropertyName("parameters")] public object Parameters { get; set; } // JSON Schema
        }

        private class ChatResponse
        {
            [JsonPropertyName("choices")] public List<Choice> Choices { get; set; }
        }

        private class Choice
        {
            [JsonPropertyName("message")] public ChoiceMessage Message { get; set; }
        }

        private class ChoiceMessage
        {
            [JsonPropertyName("role")] public string Role { get; set; }
            [JsonPropertyName("content")] public string Content { get; set; }

            // function/tool 呼び出しはここに入る
            [JsonPropertyName("tool_calls")] public List<ToolCall> ToolCalls { get; set; }
        }

        private class ToolCall
        {
            [JsonPropertyName("id")] public string Id { get; set; }
            [JsonPropertyName("type")] public string Type { get; set; }  // "function"
            [JsonPropertyName("function")] public ToolCallFunction Function { get; set; }
        }

        private class ToolCallFunction
        {
            [JsonPropertyName("name")] public string Name { get; set; }
            [JsonPropertyName("arguments")] public string Arguments { get; set; } // JSON string
        }

        // ==== 構造化入出力モデル ====

        public class Segment
        {
            [JsonPropertyName("type")] public string Type { get; set; } // "tag" | "text"
            [JsonPropertyName("value")] public string Value { get; set; }
        }

        public class SegmentResult
        {
            [JsonPropertyName("segments")] public List<Segment> Segments { get; set; }
        }

        // ==== BAKIN 制御文字トークナイザ ====

        // 代表的コマンド（必要ならここに追加）
        // \C[n], \N[name], \S[nnn], \i[x,y], \_, \., \|, \^ など
        // まずは汎用 + 代表列挙のハイブリッドで堅牢さを確保
        private static readonly Regex BakinTagRegex = new Regex(
            @"\\(?:C\[\d+\]|N\[[^\]\r\n]*\]|S\[\d+\]|i\[\d+,\d+\]|[_\.\|\^]|[A-Za-z]{1,2}(?:\[[^\]]*\])?)",
            RegexOptions.Compiled);

        private static List<Segment> Tokenize(string s)
        {
            var list = new List<Segment>();
            if (string.IsNullOrEmpty(s))
            {
                return new List<Segment> { new Segment { Type = "text", Value = "" } };
            }

            int idx = 0;
            foreach (Match m in BakinTagRegex.Matches(s))
            {
                if (m.Index > idx)
                {
                    list.Add(new Segment { Type = "text", Value = s.Substring(idx, m.Index - idx) });
                }
                list.Add(new Segment { Type = "tag", Value = m.Value });
                idx = m.Index + m.Length;
            }
            if (idx < s.Length)
            {
                list.Add(new Segment { Type = "text", Value = s.Substring(idx) });
            }
            return list;
        }

        private static string Reconstruct(List<Segment> segments)
            => string.Concat(segments.Select(x => x.Value));

        public TraleClientWebApi(IHttpClientFactory clientFactory)
        {
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        }

        private static readonly Dictionary<string, string> languageTable = new()
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
            var ret = new string[2] { "", "" };

            // ---- 必須オプションチェック ----
            if (!options.TryGetValue("Auth_key", out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
                return new[] { "", "Auth_key not set." };
            if (!options.TryGetValue("Model", out var model) || string.IsNullOrWhiteSpace(model))
                return new[] { "", "Model not set." };

            // 任意：ContentKey（用語集／文体ルール）を system に注入
            options.TryGetValue("ContentKey", out var glossaryRaw); // 任意（ユーザーがGUIで設定）

            // ---- 同言語ならスルー ----
            if (srs_lang == dst_lang)
                return new[] { "OK", source ?? "" };

            // ---- 入力のトークナイズ（制御文字と本文の分離）----
            var segments = Tokenize(source ?? "");

            // ---- system / user メッセージ ----
            var sys = BuildSystemPrompt(glossaryRaw, srs_lang, dst_lang);
            var user = BuildUserPrompt(segments, srs_lang, dst_lang);

            // ---- function calling のツール定義（返却スキーマ）----
            var tool = new ToolSpec
            {
                Function = new FunctionSpec
                {
                    Name = "return_segments",
                    Description = "Return the translated segments array. 'tag' segments must be returned verbatim. 'text' segments must be translated.",
                    Parameters = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            segments = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    additionalProperties = false,
                                    properties = new
                                    {
                                        type = new { type = "string", @enum = new[] { "tag", "text" } },
                                        value = new { type = "string" }
                                    },
                                    required = new[] { "type", "value" }
                                }
                            }
                        },
                        required = new[] { "segments" }
                    }
                }
            };

            try
            {
                var endpoint = "https://api.openai.com/v1/chat/completions";
                var req = new ChatRequest
                {
                    Model = model, // 例: "gpt-4.1"
                    Messages = new List<ChatMessage>
                    {
                        new ChatMessage { Role = "system", Content = sys },
                        new ChatMessage { Role = "user", Content = user }
                    },
                    Temperature = 0.0,
                    TopP = 1.0,
                    Tools = new List<ToolSpec> { tool },
                    ToolChoice = "required" // ← 必ず function の引数として返させる
                };

                var json = JsonSerializer.Serialize(req, new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var client = _clientFactory.CreateClient();
                client.Timeout = TimeSpan.FromMilliseconds(60000);

                var response = client.SendAsync(request).Result;
                var responseBody = response.Content.ReadAsStringAsync().Result;

                ret[0] = response.StatusCode.ToString();

                if (response.IsSuccessStatusCode)
                {
                    var parsed = JsonSerializer.Deserialize<ChatResponse>(responseBody);
                    var toolCall = parsed?.Choices?.FirstOrDefault()?.Message?.ToolCalls?.FirstOrDefault();

                    if (toolCall?.Function?.Name != "return_segments" || string.IsNullOrEmpty(toolCall.Function.Arguments))
                        return new[] { ret[0], "Unexpected response (no tool call)." };

                    // function の引数(JSON)をパース
                    var segResult = JsonSerializer.Deserialize<SegmentResult>(toolCall.Function.Arguments);
                    if (segResult?.Segments == null)
                        return new[] { ret[0], "Unexpected response (segments missing)." };

                    // tagが改変されていないか軽く検証（同数・同順）
                    if (!LightweightTagCheck(segments, segResult.Segments))
                        return new[] { ret[0], "Tag mismatch detected in model output." };

                    // セグメントを再結合
                    var merged = Reconstruct(segResult.Segments);
                    ret[1] = merged ?? "";
                }
                else
                {
                    // エラーボディそのまま返す
                    try
                    {
                        var err = JsonSerializer.Deserialize<JsonElement>(responseBody);
                        if (err.TryGetProperty("error", out var eobj) &&
                            eobj.TryGetProperty("message", out var emsg))
                        {
                            ret[1] = emsg.GetString();
                        }
                        else
                        {
                            ret[1] = response.ReasonPhrase ?? "Some errors have occurred.";
                        }
                    }
                    catch
                    {
                        ret[1] = response.ReasonPhrase ?? "Some errors have occurred.";
                    }
                }
            }
            catch (TaskCanceledException)
            {
                ret[1] = "Request timeout.";
            }
            catch (Exception e)
            {
                ret[1] = $"Some errors have occurred: {e.Message}";
            }

            return ret;
        }

        private static string BuildSystemPrompt(string glossaryRaw, string srs, string dst)
        {
            // 任意の用語集をsystemに差し込む（ContentKey）
            var glossary = string.IsNullOrWhiteSpace(glossaryRaw) ? "" :
                "\nGLOSSARY (do not translate keys, translate values consistently):\n" + glossaryRaw + "\n";

            return
$@"You are a professional game-localization translator.

RULES:
1) Do NOT modify any RPG Developer BAKIN control codes (text commands). Keep them EXACTLY as in the input.
   Examples: \C[1], \C[0], \N[Hero], \S[001], \i[0,1], \_, \., \|, \^
2) Translate only segments where type = ""text"" from {LangName(srs)} to {LangName(dst)}.
3) Preserve whitespace and line breaks exactly. Do not add or remove characters around tags.
4) Return the result ONLY via the required function call, filling the ""segments"" array. 
   For ""tag"" segments, return the original string verbatim. For ""text"" segments, return the translation.

STYLE:
- Faithful, concise. Preserve punctuation unless required by target language.
- Keep placeholders like {{PLAYER}}, %s, %1 unchanged.

{glossary}";
        }

        private static string BuildUserPrompt(List<Segment> segments, string srs, string dst)
        {
            // ユーザーには「セグメント配列」をそのまま渡す（モデルはこれを読んで function args を埋めて返す）
            var data = new SegmentResult { Segments = segments };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            return
$@"Source language: {LangName(srs)}
Target language: {LangName(dst)}

Translate the following segments. 
- Keep ""tag"" segments unchanged.
- Translate ""text"" segments.

SEGMENTS(JSON):
{json}";
        }

        private static string LangName(string code)
            => languageTable.TryGetValue(code, out var name) ? name : code;

        // 軽いタグ検証（同数・同順・中身一致）
        private static bool LightweightTagCheck(List<Segment> src, List<Segment> dst)
        {
            var srcTags = src.Where(s => s.Type == "tag").Select(s => s.Value).ToList();
            var dstTags = dst.Where(s => s.Type == "tag").Select(s => s.Value).ToList();
            if (srcTags.Count != dstTags.Count) return false;
            for (int i = 0; i < srcTags.Count; i++)
            {
                if (!string.Equals(srcTags[i], dstTags[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }
    }
}
