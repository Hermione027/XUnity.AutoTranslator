using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SimpleJSON;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using XUnity.AutoTranslator.Plugin.Core.Endpoints.Http;
using XUnity.AutoTranslator.Plugin.Core.Utilities;
using XUnity.AutoTranslator.Plugin.Core.Web;

namespace OpenAICompatibleTranslate
{
   internal class OpenAICompatibleTranslateEndpoint : HttpEndpoint
   {
      private const int DefaultMaxConcurrency = 1;
      private const int DefaultMaxTranslationsPerRequest = 1;

      public override string Id => "OpenAICompatibleTranslate";

      public override string FriendlyName => "OpenAI 兼容翻译器";

      public override int MaxConcurrency => _maxConcurrency;

      public override int MaxTranslationsPerRequest => _maxTranslationsPerRequest;

      private string _url;
      private string _apiKey;
      private string _model;
      private string _systemPrompt;
      private string _temperature;
      private int _maxConcurrency = DefaultMaxConcurrency;
      private int _maxTranslationsPerRequest = DefaultMaxTranslationsPerRequest;

      public override void Initialize( IInitializationContext context )
      {
         _url = context.GetOrCreateSetting( "OpenAICompatible", "Url", "" );
         _apiKey = context.GetOrCreateSetting( "OpenAICompatible", "ApiKey", "" );
         _model = context.GetOrCreateSetting( "OpenAICompatible", "Model", "gpt-5.5" );
         _systemPrompt = context.GetOrCreateSetting( "OpenAICompatible", "SystemPrompt", "你是一个专业翻译器。请把源文本翻译成目标语言，只返回译文，不要解释，不要 Markdown，不要代码块。" );
         _temperature = context.GetOrCreateSetting( "OpenAICompatible", "Temperature", "0" );
         _maxConcurrency = NormalizePositiveInt( context.GetOrCreateSetting( "OpenAICompatible", "MaxConcurrency", DefaultMaxConcurrency ), DefaultMaxConcurrency );
         _maxTranslationsPerRequest = NormalizePositiveInt( context.GetOrCreateSetting( "OpenAICompatible", "MaxTranslationsPerRequest", DefaultMaxTranslationsPerRequest ), DefaultMaxTranslationsPerRequest );

         if( string.IsNullOrEmpty( _url ) ) throw new EndpointInitializationException( "The OpenAICompatibleTranslate endpoint requires a Url." );
         if( string.IsNullOrEmpty( _apiKey ) ) throw new EndpointInitializationException( "The OpenAICompatibleTranslate endpoint requires an ApiKey." );
         if( string.IsNullOrEmpty( _model ) ) throw new EndpointInitializationException( "The OpenAICompatibleTranslate endpoint requires a Model." );

         if( Uri.TryCreate( _url, UriKind.Absolute, out var uri ) )
         {
            context.DisableCertificateChecksFor( uri.Host );
         }
      }

      public override void OnCreateRequest( IHttpRequestCreationContext context )
      {
         var body = new JSONObject();
         body[ "model" ] = _model;
         body[ "temperature" ] = ParseTemperature( _temperature );
         body[ "stream" ] = false;

         var messages = new JSONArray();

         var systemMessage = new JSONObject();
         systemMessage[ "role" ] = "system";
         systemMessage[ "content" ] = BuildSystemPrompt( context );
         messages.Add( systemMessage );

         var userMessage = new JSONObject();
         userMessage[ "role" ] = "user";
         userMessage[ "content" ] = BuildUserContent( context );
         messages.Add( userMessage );

         body[ "messages" ] = messages;

         var request = new XUnityWebRequest( "POST", GetChatCompletionsUrl(), body.ToString() );
         request.Headers[ "Authorization" ] = "Bearer " + _apiKey;
         request.Headers[ "Content-Type" ] = "application/json; charset=utf-8";
         request.Headers[ "Accept" ] = "application/json";

         context.Complete( request );
      }

      public override void OnExtractTranslation( IHttpTranslationExtractionContext context )
      {
         var data = context.Response.Data;
         if( string.IsNullOrEmpty( data ) ) context.Fail( "Empty response from OpenAI-compatible endpoint." );

         var obj = JSON.Parse( data );
         if( obj == null ) context.Fail( "Failed to parse JSON from OpenAI-compatible endpoint." );

         var error = obj[ "error" ];
         var errorMessage = error?[ "message" ]?.Value;
         if( !string.IsNullOrEmpty( errorMessage ) ) context.Fail( errorMessage );

         var choices = obj[ "choices" ]?.AsArray;
         if( choices == null || choices.Count == 0 ) context.Fail( "No choices were returned by the endpoint." );

         var choice = choices[ 0 ];
         if( choice == null ) context.Fail( "No choices were returned by the endpoint." );

         var message = choice[ "message" ];
         var content = message?[ "content" ]?.Value ?? choice[ "text" ]?.Value;
         if( string.IsNullOrEmpty( content ) ) context.Fail( "The endpoint returned an empty translation." );

         if( context.UntranslatedTexts.Length == 1 )
         {
            context.Complete( StripCodeFences( content ) );
            return;
         }

         var translatedTexts = TryParseTranslatedTexts( content, context.UntranslatedTexts.Length );
         if( translatedTexts == null ) context.Fail( "The endpoint returned an invalid batch translation." );

         context.Complete( translatedTexts );
      }

      private string GetChatCompletionsUrl()
      {
         return BuildChatCompletionsUrl( _url );
      }

      private static string BuildChatCompletionsUrl( string url )
      {
         if( string.IsNullOrEmpty( url ) || url.Trim().Length == 0 ) return url;

         var normalizedUrl = url.Trim().TrimEnd( '/' );
         if( normalizedUrl.EndsWith( "/chat/completions", StringComparison.OrdinalIgnoreCase ) )
         {
            return normalizedUrl;
         }

         if( !Uri.TryCreate( normalizedUrl, UriKind.Absolute, out var uri ) )
         {
            return normalizedUrl + "/chat/completions";
         }

         var builder = new UriBuilder( uri );
         var path = ( builder.Path ?? string.Empty ).TrimEnd( '/' );
         if( string.IsNullOrEmpty( path ) || path == "/" )
         {
            builder.Path = "/v1/chat/completions";
         }
         else
         {
            builder.Path = path + "/chat/completions";
         }

         return builder.Uri.AbsoluteUri.TrimEnd( '/' );
      }

      private string BuildSystemPrompt( IHttpTranslationContext context )
      {
         var prompt = _systemPrompt;
         if( string.IsNullOrEmpty( prompt ) )
         {
            prompt = "你是一个专业翻译器。请把源文本翻译成目标语言，只返回译文，不要解释，不要 Markdown，不要代码块。";
         }

         prompt = prompt
            .Replace( "{SourceLanguage}", context.SourceLanguage ?? string.Empty )
            .Replace( "{FromLanguage}", context.SourceLanguage ?? string.Empty )
            .Replace( "{TargetLanguage}", context.DestinationLanguage ?? string.Empty )
            .Replace( "{DestinationLanguage}", context.DestinationLanguage ?? string.Empty )
            .Replace( "{ToLanguage}", context.DestinationLanguage ?? string.Empty );

         var builder = new StringBuilder();
         builder.AppendLine( prompt );
         builder.Append( "Source language: " ).Append( context.SourceLanguage ?? string.Empty ).AppendLine();
         builder.Append( "Target language: " ).Append( context.DestinationLanguage ?? string.Empty ).AppendLine();
         builder.AppendLine( "Rules:" );
         builder.AppendLine( "- Translate only the user content." );
         builder.AppendLine( "- For a single item, return only the translated text." );
         builder.AppendLine( "- For multiple items, return only a JSON array of strings in the same order." );
         builder.AppendLine( "- Do not wrap the result in markdown or code fences." );
         builder.Append( "- Do not add explanations." );
         return builder.ToString();
      }

      private string BuildUserContent( IHttpTranslationContext context )
      {
         if( context.UntranslatedTexts.Length == 1 )
         {
            return context.UntranslatedText ?? string.Empty;
         }

         var builder = new StringBuilder();
         builder.AppendLine( "Input JSON array:" );
         builder.Append( BuildInputArray( context.UntranslatedTexts ) );
         return builder.ToString();
      }

      private static string BuildInputArray( string[] texts )
      {
         var builder = new StringBuilder();
         builder.Append( "[" );
         for( int i = 0 ; i < texts.Length ; i++ )
         {
            if( i > 0 ) builder.Append( ", " );
            builder.Append( "\"" );
            builder.Append( JsonHelper.Escape( texts[ i ] ?? string.Empty ) );
            builder.Append( "\"" );
         }
         builder.Append( "]" );
         return builder.ToString();
      }

      private static string[] TryParseTranslatedTexts( string content, int expectedCount )
      {
         var normalized = StripCodeFences( content );
         var jsonFragment = ExtractJsonFragment( normalized );
         if( !string.IsNullOrEmpty( jsonFragment ) )
         {
            var parsed = JSON.Parse( jsonFragment );
            var translatedTexts = ExtractTranslatedTexts( parsed, expectedCount );
            if( translatedTexts != null )
            {
               return translatedTexts;
            }
         }

         return TrySplitLines( normalized, expectedCount );
      }

      private static string[] ExtractTranslatedTexts( JSONNode parsed, int expectedCount )
      {
         if( parsed == null ) return null;

         var array = parsed.AsArray;
         if( array != null )
         {
            return ReadTranslatedArray( array, expectedCount );
         }

         var translations = parsed[ "translations" ]?.AsArray;
         if( translations != null )
         {
            return ReadTranslatedArray( translations, expectedCount );
         }

         return null;
      }

      private static string[] ReadTranslatedArray( JSONArray array, int expectedCount )
      {
         if( array == null || array.Count != expectedCount ) return null;

         var translatedTexts = new string[ array.Count ];
         for( int i = 0 ; i < array.Count ; i++ )
         {
            var translatedText = ExtractNodeText( array[ i ] );
            if( string.IsNullOrEmpty( translatedText ) ) return null;
            translatedTexts[ i ] = translatedText;
         }

         return translatedTexts;
      }

      private static string ExtractNodeText( JSONNode node )
      {
         if( node == null ) return null;

         var value = node.Value;
         if( !string.IsNullOrEmpty( value ) )
         {
            return value;
         }

         var translation = node[ "translation" ]?.Value ?? node[ "text" ]?.Value ?? node[ "content" ]?.Value;
         if( !string.IsNullOrEmpty( translation ) )
         {
            return translation;
         }

         var token = node.ToString();
         if( string.IsNullOrEmpty( token ) ) return token;

         if( token.Length >= 2 && token[ 0 ] == '"' && token[ token.Length - 1 ] == '"' )
         {
            return JsonHelper.Unescape( token.Substring( 1, token.Length - 2 ) );
         }

         return token;
      }

      private static string StripCodeFences( string content )
      {
         if( string.IsNullOrEmpty( content ) ) return content;

         var trimmed = content.Trim();
         if( !trimmed.StartsWith( "```", StringComparison.Ordinal ) ) return trimmed;

         var firstNewLine = trimmed.IndexOfAny( new[] { '\r', '\n' } );
         if( firstNewLine < 0 ) return trimmed.Trim( '`' );

         var lastFence = trimmed.LastIndexOf( "```", StringComparison.Ordinal );
         if( lastFence > firstNewLine )
         {
            return trimmed.Substring( firstNewLine, lastFence - firstNewLine ).Trim( '\r', '\n', ' ' );
         }

         return trimmed.Substring( firstNewLine ).Trim( '\r', '\n', ' ' );
      }

      private static string ExtractJsonFragment( string content )
      {
         if( string.IsNullOrEmpty( content ) ) return null;

         var inString = false;
         var escaping = false;
         var start = -1;
         var open = '\0';
         var close = '\0';
         var depth = 0;

         for( int i = 0 ; i < content.Length ; i++ )
         {
            var c = content[ i ];

            if( start < 0 )
            {
               if( c == '{' || c == '[' )
               {
                  start = i;
                  open = c;
                  close = c == '{' ? '}' : ']';
                  depth = 1;
               }

               continue;
            }

            if( inString )
            {
               if( escaping )
               {
                  escaping = false;
                  continue;
               }

               if( c == '\\' )
               {
                  escaping = true;
                  continue;
               }

               if( c == '"' )
               {
                  inString = false;
               }

               continue;
            }

            if( c == '"' )
            {
               inString = true;
               continue;
            }

            if( c == open )
            {
               depth++;
            }
            else if( c == close )
            {
               depth--;
               if( depth == 0 )
               {
                  return content.Substring( start, i - start + 1 );
               }
            }
         }

         return null;
      }

      private static string[] TrySplitLines( string content, int expectedCount )
      {
         if( string.IsNullOrEmpty( content ) ) return null;

         var normalized = content.TrimEnd( '\r', '\n' );
         var lines = normalized.Split( '\n' );
         if( lines.Length != expectedCount ) return null;

         for( int i = 0 ; i < lines.Length ; i++ )
         {
            if( lines[ i ] == null ) lines[ i ] = string.Empty;
            if( lines[ i ].Length > 0 && lines[ i ][ lines[ i ].Length - 1 ] == '\r' )
            {
               lines[ i ] = lines[ i ].Substring( 0, lines[ i ].Length - 1 );
            }
         }

         return lines;
      }

      private static int NormalizePositiveInt( int value, int defaultValue )
      {
         return value > 0 ? value : defaultValue;
      }

      private static float ParseTemperature( string temperature )
      {
         if( float.TryParse( temperature, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value ) )
         {
            if( value < 0f ) return 0f;
            if( value > 2f ) return 2f;
            return value;
         }

         return 0f;
      }
   }
}
