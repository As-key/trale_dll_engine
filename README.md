# trale_dll_engine
This is a project for a translation engine(dll) and definition files that can be used in TRALE.<br>
It does not include TRALE itself.

このプロジェクトは、TRALE向けの翻訳エンジン(dll)を扱っています。<br>
TRALE本体のソースコードは含みません。
<br><br>

作成したdll/jsonファイルは、TRALE本体と同じ場所にあるengineフォルダに格納してください。TRALE起動時に自動的に定義情報が読み込まれます。</br>

### TRALE Web site :
https://trale.org/
<br><br>

# JSON File

## dllに対する設定はJSONファイルで定義されます。
※JSONファイルは、dllの場合もps1の場合も同一構造です。
JSONファイルを構成する要素は４種類です。<br>
	
	"Info"
	"Option"
	"SourceLanguage"
	"TargetLanguage"

各要素は次の型で表現されます。<br>
	
	{
		"name": "",
		"value": "",
		"note": "",
	}

## InfoではAPIの定義を行います。
API本体(エンジンファイル本体)に対して、複数のJSONファイルを定義することで、定義ファイルを複数定義することで、同じdllに対して異なる設定を行うことが可能です。<br>

	"Info": {
		"name": "DeepL API Free V2 (ps1)",  //TRALEのDLL選択リストに表示されます。
		"value": "DeepL_API_Free.dll",  //利用するDLL本体を記載します。
		"note": "Translation using the API of DeepL free."  //TRALEのAPI説明欄に表示される説明文です。
	},
	......

## Optionでは引数を定義します。この定義は配列として複数定義可能です。
翻訳エンジンのAuth keyや、設定ファイルを指定する為に利用します。<br>
引数名、引数パラメータとして利用されますので、名称は英数字及び_で簡潔に表現してください。<br>
特殊文字を指定したい場合は、エンジンファイル本体に記述するなどしてください。<br>

	"Option": [
	{
		"name": "Auth_key",  //変数名
		"value": "",
		"note": "Set the Auth key that you obtained on the account page of DeepL."　　//変数の説明
	},
	......

## SourceLanguageとTargetLanguageには、言語に対するコードを指定します。
	SourceLanguage
	TargetLanguage

### この定義は配列で定義可能です。
	"TargetLanguage": [
		{
			"name": "bg",  //TARLE側の言語コード
			"value": "BG",  //API本体(翻訳エンジン)に渡される言語コード
			"note": "Bulgarian"
		},
	......

基本には翻訳エンジンの仕様に合わせて言語コードを定義してください。<br>
例えばDEEPLでは、<br>
英語(Source)→日本語(Target)の翻訳を行う場合、<br>
Source=EN, Target=JA 指定しますが、<br>
日本語(Source)→英語(Target)の翻訳を行う場合は、<br>
Source=JA, Target=EN-US (又はEN-GB) といった指定になります。<br>
TRALE上では、EN,EN-US,EN-GBは別々に管理されますので、<br>
全てENとして管理したい場合は次のように定義しておくと。取り扱いが簡単です。<br>

	"TargetLanguage": [
		{
			"name": "en",
			"value": "EN-US",
			"note": "English"
		},
	......
<br>

# DLL File
翻訳エンジンなどを呼び出す実体です。<br>
<br>

## DLL interface
DLL interfaceは非常にシンプルです。
実装例をサンプルコードに沿って解説します。

    namespace TraleDLLManager
    {
        public interface InterfaceTraleDLL
        {
            public string GetDllName();
            public string[] doTranslate(string srs_lang,
                                string source,
                                string dst_lang,
                                Dictionary<string, string>  options);
        }
    }


## public string GetDllName()
TRALE上で表示されるDLL名称です。
他のDLLの名称と同一の名前にならないようにしてください。

    public string GetDllName()
    {
        return "DeepL API Free for TRALE";
    }

## Constructor
接続にはHTTPClientを利用します。
HTTPClientは、DLL定義毎に管理されます。
※JSON Fileで、1つのDLLに対して複数の使い方を定義した場合、共通のHTTPClient上で処理されます。

    public TraleTranslatorDLL()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddHttpClient();
        services.AddSingleton<TraleClientWebApi>();
        var provider = services.BuildServiceProvider();
        traleClientWebApi = provider.GetRequiredService<TraleClientWebApi>();
    }

## public string[] doTranslate(...)
実際に翻訳エンジンを呼び出す処理を実装します。
（翻訳以外…例えば校正ツールなどにも利用可能です。）
sample codeでは、「public class TraleClientWebApi」に処理を実装しています。

    public string[] doTranslate(string srs_lang,
                                string source,
                                 string dst_lang,
                                 Dictionary<string, string> options)
    {
        return traleClientWebApi.translator(srs_lang, source, dst_lang, options);
    }

返却型は、string[]です。
string[0] : 出力結果
string[1] : エラー内容
が規定の形式となっています。

第4引数の「options」は、
Dictionary-key : JSONで定義された定義名
Dictionary-value : TRALE上で設定された文字列
となります。
必要に応じて処理を実装ください。

注意
HttpClient は、IHttpClientFactory で管理されますので、個別にDisposeなどは行わないでください。（両者の取り扱いについてはMSDN等を参照してください）

## エラーコードについて
エラー内容は、string[1] に格納してください。

## 文章の受け渡しについて
出力結果の文章は、string[0] に格納してください。

ps1実装のAPIでは、BASE64デコード/エンコードが必要ですが、
dll実装のAPIでは、直接string型での入出力となります。BASE64 向けの変換処理は必要ありません。
