using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace RealDiceCommon
{
    // XXX IDisposable
    public class HttpRouter
    {
        public delegate Task HttpHandler(HttpListenerContext context);

        private HttpListener _listner;
        private IDictionary<string, HttpHandler> _handlerMap;

        public HttpRouter(string[] prefixes)
        {
            _listner = new HttpListener();
            foreach (var prefix in prefixes)
            {
                _listner.Prefixes.Add(prefix);
            }
            _handlerMap = new Dictionary<string, HttpHandler>();
        }

        public void Start()
        {
            _listner.Start();
            _listner.BeginGetContext(OnRequested, this);
        }

        public void Stop()
        {
            _listner.Stop();
            _listner.Close();
        }

        public void Register(string path, HttpHandler handler)
        {
            _handlerMap[path] = handler;
        }

        static void OnRequested(IAsyncResult result)
        {
            var router = (HttpRouter)result.AsyncState;
            var listener = router._listner;
            listener.BeginGetContext(OnRequested, router);

            var context = listener.EndGetContext(result);
            var request = context.Request;
            var response = context.Response;

            try
            {
                Console.WriteLine(request.HttpMethod + " " + request.Url);
                var path = request.Url.LocalPath;
                if (router._handlerMap.ContainsKey(path))
                {
                    var handler = router._handlerMap[path];
                    handler(context).Wait();
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    response.ContentType = "text/plain";
                    response.OutputStream.WriteString("Not Found");
                    response.Close();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);

                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                response.ContentType = "text/plain";
                response.OutputStream.WriteString(e.Message);
                response.Close();
            }
        }
    }

    public static class StreamExtension
    {
        public static void WriteString(this Stream stream, string value)
        {
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(value);
            }
        }
    }
}
