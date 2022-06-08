namespace TraleDLLManager
{
    public interface InterfaceTraleDLL
    {
        public string GetDllName();

        public string[] doTranslate(string srs_lang,
                            string source,
                            string dst_lang,
                            Dictionary<string, string> options);
    }
}
