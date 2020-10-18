using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MiddlewareWithUI
{
    public class CustomUIMiddleware
    {
        private const string RoutePrefix = "custom/ui";
        private readonly StaticFileMiddleware _staticFileMiddleware;

        public CustomUIMiddleware(
            RequestDelegate next,
            IWebHostEnvironment hostingEnv,
            ILoggerFactory loggerFactory)
        {
            _staticFileMiddleware = CreateStaticFileMiddleware(next, hostingEnv, loggerFactory);
        }

        public async Task Invoke(HttpContext httpContext)
        {
            var httpMethod = httpContext.Request.Method;
            var path = httpContext.Request.Path.Value;

            // If the RoutePrefix is requested (with or without trailing slash), redirect to index URL
            if (httpMethod == "GET" && Regex.IsMatch(path, $"^/?{Regex.Escape(RoutePrefix)}/?$", RegexOptions.IgnoreCase))
            {
                var indexUrl = httpContext.Request.GetEncodedUrl().TrimEnd('/') + "/index";
                RespondWithRedirect(httpContext.Response, indexUrl);
                return;
            }

            // Serve the index.html file
            if (httpMethod == "GET" && Regex.IsMatch(path, $"^/{Regex.Escape(RoutePrefix)}/?index", RegexOptions.IgnoreCase))
            {
                await RespondWithIndexHtml(httpContext.Response, path);
                return;
            }

            if (IsInEmbeddedResource(path, out _))
            {
                await RespondWithIndexHtml(httpContext.Response, path);
                return;
            }

            // Serve a static file or continue to the next middleware
            await _staticFileMiddleware.Invoke(httpContext);
        }

        private StaticFileMiddleware CreateStaticFileMiddleware(
            RequestDelegate next,
            IWebHostEnvironment hostingEnv,
            ILoggerFactory loggerFactory)
        {
            var staticFileOptions = new StaticFileOptions
            {
                RequestPath = $"/{RoutePrefix}",
                FileProvider = new EmbeddedFileProvider(typeof(CustomUIMiddleware).GetTypeInfo().Assembly),
            };

            return new StaticFileMiddleware(next, hostingEnv, Options.Create(staticFileOptions), loggerFactory);
        }

        private void RespondWithRedirect(HttpResponse response, string location)
        {
            response.StatusCode = 301;
            response.Headers["Location"] = location;
        }

        private bool IsInEmbeddedResource(string location, out string resourceName)
        {
            var file = location.Replace(RoutePrefix, "").Trim('/').Replace("/", ".");
            resourceName = typeof(CustomUIMiddleware).GetTypeInfo().Assembly.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith(file));
            if (resourceName == null)
                return false;
            else
                return true;
        }

        private string GetExtension(string location, bool withDot = false)
        {
            var ext = Path.GetExtension(location);
            if (withDot)
                return ext;
            else
                return ext.TrimStart('.');
        }

        private static byte[] ToByteArray(Stream input)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                input.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private async Task RespondWithIndexHtml(HttpResponse response, string location)
        {
            var resourceName = "";
            var status = IsInEmbeddedResource(location, out resourceName);
            if (!status) return;

            var ext = GetExtension(resourceName, true);
            response.StatusCode = 200;

            var images = new[] {
                ".apng", ".bmp", ".gif", ".ico", ".cur", ".jpg", ".jpeg", ".jfif", ".pjpeg", ".pjp",
                ".png", ".svg", ".tif", ".tiff", ".webp"
            };

            await using var stream = typeof(CustomUIMiddleware).GetTypeInfo().Assembly.GetManifestResourceStream(resourceName);
            response.ContentType = $"{MimeTypeLookup.GetMimeType(ext)};charset=utf-8";

            if (images.Contains(ext))
            {
                await response.BodyWriter.WriteAsync(ToByteArray(stream));
                return;
            }

            var htmlBuilder = new StringBuilder(await new StreamReader(stream).ReadToEndAsync());
            await response.WriteAsync(htmlBuilder.ToString(), Encoding.UTF8);
        }
    }
}
