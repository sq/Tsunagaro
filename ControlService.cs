using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Newtonsoft.Json;
using Squared.Task;
using Squared.Task.Http;
using Squared.Task.IO;
using System.Linq;

namespace Tsunagaro {
    public class ControlService {
        bool IsInitialized = false;

        const int PortBase = 9888;
        const int PortsToTry = 10;

        public int Port { get; private set; }
        public string HostName { get; private set; }
        public string URL { get; private set; }

        public readonly Dictionary<string, Func<HttpServer.Request, IEnumerator<object>>> Handlers;
        private readonly TaskScheduler Scheduler;
        private readonly HttpServer HttpServer;

        public ControlService (TaskScheduler scheduler) {
            Scheduler = scheduler;
            HttpServer = new HttpServer(Scheduler);

            Handlers =
                new Dictionary<string, Func<HttpServer.Request, IEnumerator<object>>> {
                    {"/", ServeIndex}
                };
        }

        public IEnumerator<object> Initialize () {
            if (IsInitialized)
                yield break;

            IsInitialized = true;

            Port = -1;
            // Scan ports starting from our port base to find one we can use.
            for (int portToTry = PortBase; portToTry < (PortBase + PortsToTry); portToTry++) {
                var ep = new IPEndPoint(IPAddress.Any, portToTry);
                var f = Future.RunInThread(() => IsEndPointBound(ep));
                yield return f;

                if (!f.Result) {
                    HttpServer.EndPoints.Add(ep);
                    Port = portToTry;
                    HostName = Dns.GetHostName();
                    URL = String.Format("http://{0}:{1}/", HostName, Port);
                    break;
                }
            }

            if (Port == -1) {
                IsInitialized = false;
                throw new Exception("Could not find a port to bind the http server to");
            }

            Scheduler.Start(HttpTask(), TaskExecutionPolicy.RunAsBackgroundTask);

            Console.WriteLine("Control service active at {0}", URL);
        }

        private static bool IsEndPointBound (EndPoint endPoint) {
            try {
                using (var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.IP)) {
                    socket.Bind(endPoint);
                }

                return false;
            } catch (SocketException se) {
                const int endPointAlreadyBound = 10048;

                if (se.ErrorCode == endPointAlreadyBound)
                    return true;

                throw;
            }
        }

        IEnumerator<object> HttpTask () {
            yield return HttpServer.StartListening();

            while (true) {
                var fRequest = HttpServer.AcceptRequest();
                yield return fRequest;

                var fRequestTask = Scheduler.Start(RequestTask(fRequest.Result));

                fRequestTask.RegisterOnComplete(HandleRequestError);
            }
        }

        public string NormalizeFilename (string filename) {
            var result = filename.ToLowerInvariant();

            if (Path.DirectorySeparatorChar != '/')
                result = result.Replace('/', Path.DirectorySeparatorChar);
            if (Path.DirectorySeparatorChar != '\\')
                result = result.Replace('\\', Path.DirectorySeparatorChar);

            return result;
        }

        static void HandleRequestError (IFuture future) {
            if (future.Failed)
                Console.WriteLine("Unhandled error in request task: {0}", future.Error);
        }

        IEnumerator<object> RequestTask (HttpServer.Request request) {
            using (request) {
                IEnumerator<object> task = null;
                var path = request.Line.Uri.AbsolutePath;

                var normalizedPath = path.ToLowerInvariant().Trim();
                if (Handlers.ContainsKey(normalizedPath)) {
                    var handler = Handlers[normalizedPath];
                    task = handler(request);
                } else if (path == "/") {
                    path = "/html/index.html";
                }

                if (task == null)
                    task = ServeIncludedStaticFile(request, path);

                if (task == null)
                    task = ServeError(request, 404, "Invalid address: " + path);

                var fTask = Scheduler.Start(task);
                yield return fTask;

                if (fTask.Failed) {
                    Console.WriteLine("Error in handler for '{0}': {1}", path, fTask.Error);

                    try {
                        request.Response.StatusCode = 500;
                        request.Response.StatusText = "Internal server error";

                        var errorObject = new Dictionary<string, object> {
                            {"error", fTask.Error.GetType().Name},
                            {"message", fTask.Error.Message},
                            {"detail", fTask.Error.ToString()}
                        };

                        WriteResponseBody(request, JsonConvert.SerializeObject(errorObject));
                    } catch (InvalidOperationException) {
                    }
                }
            }
        }

        private static void CopyStream (Stream input, Stream output) {
            byte[] buffer = new byte[1024 * 64];

            while (true) {
                int read = input.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                    return;
                output.Write(buffer, 0, read);
            }
        }

        private static IEnumerator<object> CopyStreamAsync (Stream input, Stream output) {
            const int bufferSize = 1024 * 128;
            byte[] buffer = new byte[bufferSize];

            using (var sdaIn = new StreamDataAdapter(input, false))
            using (var sdaOut = new StreamDataAdapter(output, false))
            while (true) {
                var fBytesRead = sdaIn.Read(buffer, 0, buffer.Length);
                yield return fBytesRead;

                if (fBytesRead.Result <= 0)
                    yield break;

                yield return sdaOut.Write(buffer, 0, fBytesRead.Result);
            }
        }

        private static string PickContentType (string path, out bool expandExpressions) {
            var ext = (Path.GetExtension(path) ?? "").ToLower();
            expandExpressions = false;

            switch (ext) {
                case ".png":
                    return "image/png";

                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";

                case ".html":
                case ".htm":
                    expandExpressions = true;
                    return "text/html; charset=utf-8";

                case ".css":
                    return "text/css";
                
                case ".js":
                    expandExpressions = true;
                    return "text/javascript";

                default:
                    return "application/octet-stream";
            }
        }

        private string ResolveLocalPath (string path) {
            if (path.StartsWith("/"))
                path = path.Substring(1);

            string localPath;

            if (Debugger.IsAttached && Directory.Exists(Path.Combine("..", "..", "web_assets"))) {
                // Pull from project folder instead of build
                localPath = Path.Combine("..", "..", "web_assets", path);
            } else {
                localPath = Path.Combine(".", "web_assets", path);
            }

            return localPath;
        }

        static bool CheckETag (HttpServer.Request request, string filename) {
            var lastWriteTime = File.GetLastWriteTimeUtc(filename);
            var eTag = String.Format("\"{0:X8}\"", lastWriteTime.ToBinary());

            request.Response.CacheControl = "must-revalidate";

            string ifNoneMatch = request.Headers.GetValue("If-None-Match");
            if (ifNoneMatch == eTag) {
                request.Response.StatusCode = 304;
                request.Response.StatusText = "Not Modified";
                return true;
            }

            request.Response.Headers.SetValue("ETag", eTag);
            return false;
        }

        private IEnumerator<object> ServeIncludedStaticFile (HttpServer.Request request, string path) {
            bool expandExpressions;
            string mimeType;

            var localPath = ResolveLocalPath(path);

            if (File.Exists(localPath)) {
                if (CheckETag(request, localPath)) {
                    yield return request.Response.SendHeaders();
                    yield break;
                }

                mimeType = PickContentType(path, out expandExpressions);

                if (expandExpressions)
                    yield return ServeExpandedText(request, () => File.OpenRead(localPath), mimeType);
                else
                    yield return ServeStaticFile(request, () => File.OpenRead(localPath), mimeType);
            }
        }

        private static readonly Regex ExpansionRegex =
            new Regex(@"\<\%\=(?'expression'[^\%]*)\%\>", RegexOptions.Compiled);

        string ExpressionExpander (Match m, Dictionary<string, object> extraParameters = null) {
            var expression = m.Groups["expression"].Value;

            object value = null;
            var tThis = this.GetType();
            var field = tThis.GetField(expression, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var property = tThis.GetProperty(expression, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (field != null) {
                value = field.GetValue(this);
            } else if (property != null) {
                value = property.GetValue(this, null);
            } else if (extraParameters != null) {
                extraParameters.TryGetValue(expression, out value);
            } else {
                return String.Format("(/* invalid expression '{0}' */ undefined)", expression);
            }

            string valueString = JsonConvert.SerializeObject(value);

            return String.Format("(/* {0} */ {1})", expression, valueString);
        }

        public static IFuture WriteResponseBody (HttpServer.Request request, byte[] body) {
            if (body == null)
                return request.Response.SendHeaders();

            return request.Response.SendResponse(new ArraySegment<byte>(body, 0, body.Length));
        }

        public static IFuture WriteResponseBody (HttpServer.Request request, Stream body) {
            if (body == null)
                return request.Response.SendHeaders();

            return request.Response.SendResponse(body);
        }

        public static IFuture WriteResponseBody (HttpServer.Request request, string text) {
            if (text == null)
                return request.Response.SendHeaders();

            return request.Response.SendResponse(text, new UTF8Encoding(false));
        }

        public IEnumerator<object> ServeExpandedText (HttpServer.Request request, Func<Stream> openSource, string contentType, Dictionary<string, object> extraParameters = null) {
            request.Response.ContentType = contentType;

            using (var ar = new AsyncTextReader(new StreamDataAdapter(openSource(), true))) {
                var fText = ar.ReadToEnd();
                yield return fText;

                var expandedText = ExpansionRegex.Replace(fText.Result ?? "", (m) => ExpressionExpander(m, extraParameters));

                yield return WriteResponseBody(request, expandedText);
            }
        }

        public static IEnumerator<object> ServeStaticFile (HttpServer.Request request, Func<Stream> openSource, string contentType) {
            request.Response.StatusCode = 200;
            request.Response.ContentType = contentType;

            using (var src = openSource()) {
                yield return WriteResponseBody(request, src);
            }
        }

        public static IEnumerator<object> ServeError (HttpServer.Request request, int errorCode, string errorText) {
            request.Response.StatusCode = errorCode;
            request.Response.StatusText = errorText;
            request.Response.ContentType = "text/html; charset=utf-8";

            yield return WriteResponseBody(request, String.Format(
                "<html><head><title>Error {0}</title></head><body>{1}</body></html>",
                errorCode,
                WebUtility.HtmlEncode(errorText)
            ));
        }

        static string UnescapeURL (string url) {
            // God, why did they deprecate the builtin for this without a replacement???
            return (new Uri("http://augh/" + url))
                .GetComponents(UriComponents.Path, UriFormat.Unescaped)
                .Replace("+", " ")
                .Replace('/', Path.DirectorySeparatorChar);
        }

        IEnumerator<object> ServeIndex (HttpServer.Request request) {
            request.Response.ContentType = "text/html; charset=utf-8";

            var l = Program.StdOut.Length;
            var b = Program.StdOut.GetBuffer();
            var logText = (new UTF8Encoding(false)).GetString(b, 0, (int)l);

            var html = String.Format(
                @"<html>
    <head>
        <title>Status</title>
        <meta charset=""UTF-8"">
        <meta http-equiv=""refresh"" content=""10"">
    </head>
    <body>
        <h2>Peers</h2>
        <table>
            <tr><th>Name</th><th>Address</th></tr>
{1}
        </table>
        <h2>Log</h2>
        <pre>
{0}
        </pre>
    </body>
</html>",
                HttpUtility.HtmlEncode(logText),
                String.Join(
                    Environment.NewLine,
                    from p in Program.Peer.Peers.Values
                    select String.Format(
                        "<tr><td>{0}</td><td>{1}</td></tr>",
                        p.HostName,
                        p.RemoteEndPoint
                    )
                )
            );

            yield return WriteResponseBody(request, html);
        }
    }
}
