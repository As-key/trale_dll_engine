using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using TraleDLLManager;

namespace Offline_OPUSMT
{
    public class TraleTranslatorDLL : InterfaceTraleDLL
    {
        private TraleOfflineOpusMT translator;

        public string GetDllName()
        {
            return "Offline OPUS-MT (Embedded Python)";
        }

        public TraleTranslatorDLL()
        {
            translator = new TraleOfflineOpusMT();
        }

        public string[] doTranslate(
            string srs_lang,
            string source,
            string dst_lang,
            Dictionary<string, string> options)
        {
            return translator.Translate(srs_lang, source, dst_lang, options);
        }
    }

    public class TraleOfflineOpusMT
    {
        public string[] Translate(
            string srs_lang,
            string source,
            string dst_lang,
            Dictionary<string, string> options)
        {
            string[] ret = new string[2] { "", "" };

            try
            {
                string key = (srs_lang + "->" + dst_lang).ToLowerInvariant();

                switch (key)
                {
                    case "ja->en":
                        ret[1] = RunPython("ja", "en", source ?? "");
                        ret[0] = "OK";
                        break;

                    case "en->ja":
                        ret[1] = RunPython("en", "ja", source ?? "");
                        ret[0] = "OK";
                        break;

                    default:
                        ret[1] = "unsupported language pair: " + key;
                        break;
                }
            }
            catch (Exception ex)
            {
                ret[1] = ex.Message;
            }

            return ret;
        }

        private string RunPython(string src, string dst, string text)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string pythonExe = Path.Combine(baseDir, "python", "python.exe");
            string worker = Path.Combine(baseDir, "python", "translate_worker.py");

            string srcModel = Path.Combine(baseDir, "models", src + "-" + dst + "-src");
            string ct2Model = Path.Combine(baseDir, "models", src + "-" + dst);

            if (!File.Exists(pythonExe))
                throw new Exception("python.exe not found: " + pythonExe);

            if (!File.Exists(worker))
                throw new Exception("translate_worker.py not found: " + worker);

            if (!Directory.Exists(srcModel))
                throw new Exception("src model not found: " + srcModel);

            if (!Directory.Exists(ct2Model))
                throw new Exception("ct2 model not found: " + ct2Model);

            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments =
                    "\"" + worker + "\" " +
                    "\"" + srcModel + "\" " +
                    "\"" + ct2Model + "\" " +
                    "\"" + EscapeCommandLineArg(text ?? "") + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var p = Process.Start(psi);
            if (p == null)
                throw new Exception("python start failed");

            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (string.IsNullOrWhiteSpace(stdout))
            {
                if (!string.IsNullOrWhiteSpace(stderr))
                    throw new Exception(stderr);

                throw new Exception("python returned empty");
            }

            using var doc = JsonDocument.Parse(stdout);

            bool ok = doc.RootElement.GetProperty("ok").GetBoolean();
            if (!ok)
            {
                string err = "";
                if (doc.RootElement.TryGetProperty("error", out var errProp))
                    err = errProp.GetString() ?? "unknown";
                else
                    err = "unknown";

                if (!string.IsNullOrWhiteSpace(stderr))
                    err += " | stderr: " + stderr;

                throw new Exception(err);
            }

            if (doc.RootElement.TryGetProperty("text", out var textProp))
                return textProp.GetString() ?? "";

            return "";
        }

        private string EscapeCommandLineArg(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";

            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}