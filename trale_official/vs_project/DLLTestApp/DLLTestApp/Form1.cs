using System;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;


namespace DLLTestApp
{
    public partial class Form1 : Form
    {
        private HttpClientWebApi httpClientWebApi;

        private Object dllobj;
        private MethodInfo translator;

        public Form1()
        {
            InitializeComponent();

            IServiceCollection services = new ServiceCollection();
            services.AddHttpClient();
            services.AddSingleton<HttpClientWebApi>();
            var provider = services.BuildServiceProvider();
            httpClientWebApi = provider.GetRequiredService<HttpClientWebApi>();
        }

        private async void buttonPost_Click(object sender, EventArgs e)
        {
            string src_lang = "EN";
            string source = "Hello world!!";
            string dst_lang = "JA";
            Dictionary<string, string> options = new Dictionary<string, string>();
            options.Add("auth_key", "32871872-635d-fce2-b55f-c4f63983b4ea");

            string[] data = await httpClientWebApi.PostData(src_lang, source, dst_lang, options);
            textBox1.Text = data[1];
        }

        private void buttonLoadDLL_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.InitialDirectory = "c:\\";
            openFileDialog.Filter = "txt files (*.txt)|*.txt|All files (*.*)|*.*";
            openFileDialog.FilterIndex = 2;
            openFileDialog.RestoreDirectory = true;

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                Assembly asm = Assembly.LoadFrom(openFileDialog.FileName);
                var module = asm.GetModule("DeepL_API_Pro.dll");
                //var translatorApi = module.GetType("TRALE_TRANSLATOR_DLL.TraleTranslatorDLL");
                var translatorApi = module.GetType("DeepL_API_Pro.TraleTranslatorDLL");

                if (translatorApi != null)
                {
                    string src_lang = "EN";
                    string source = "Hello world!!";
                    string dst_lang = "JA";
                    Dictionary<string, string> options = new Dictionary<string, string>();
                    options.Add("auth_key", "32871872-635d-fce2-b55f-c4f63983b4ea");

                    dynamic translator = Activator.CreateInstance(translatorApi);
                    string[] data = translator.doTranslate(src_lang, source, dst_lang, options);
                    textBox1.Text = data[1];
                }
            }
        }

        private void buttonPostDLL_Click(object sender, EventArgs e)
        {
            string src_lang = "EN";
            string source = "Hello world!!";
            string dst_lang = "JA";
            Dictionary<string, string> options = new Dictionary<string, string>();
            options.Add("auth_key", "32871872-635d-fce2-b55f-c4f63983b4ea");

            object ret = translator.Invoke(dllobj, new object[] { src_lang, source, dst_lang, options});

            textBox2.Text = "";
        }
    }
}
