using Serilog.Events;
using Serilog;
using System.Net;


namespace APIUploadBlobTF
{
    public class DDog
    {
        public static void SLog(string sMessage, LogEventLevel logLevel, string sTags = "", string? sHost = "", string? sService = "")
        {
            if (string.IsNullOrEmpty(sHost))
                sHost = Environment.GetEnvironmentVariable("LogHost");
            if (string.IsNullOrEmpty(sService))
                sService = Environment.GetEnvironmentVariable("LogService");

            string? sAPIKey = Environment.GetEnvironmentVariable("DD_API_KEY");

            string[] sTagArray = [];
            if (!string.IsNullOrEmpty(sTags))
                sTagArray = sTags.Split(',');

            string? sMinimum = Environment.GetEnvironmentVariable("MinLevel");
            if (!string.IsNullOrEmpty(sMinimum)) sMinimum = sMinimum.ToLower();
            else sMinimum = "";

            ServicePointManager.SecurityProtocol = (SecurityProtocolType)0xc00;
            var log = new LoggerConfiguration();
            log.WriteTo.DatadogLogs(sAPIKey, source: "csharp", host: sHost, service: sService, tags: sTagArray);

            if (sMinimum.Equals("verbose")) log.MinimumLevel.Verbose();
            else if (sMinimum.Equals("debug")) log.MinimumLevel.Debug();
            else if (sMinimum.Equals("error")) log.MinimumLevel.Error();
            else if (sMinimum.Equals("fatal")) log.MinimumLevel.Fatal();
            else if (sMinimum.Equals("info")) log.MinimumLevel.Information();
            else if (sMinimum.Equals("warning")) log.MinimumLevel.Warning();
            else log.MinimumLevel.Verbose();

            var logger = log.CreateLogger();
            logger.Write(logLevel, DateTime.Now.ToString() + " " + logLevel.ToString() + ": " + sMessage);
            logger.Dispose();
        }
    }
}
