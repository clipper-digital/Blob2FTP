using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Serilog.Events;


namespace APIUploadBlobTF
{
    public class APIUploadTrigger
    {
        private readonly ILogger _logger;

        public APIUploadTrigger(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<APIUploadTrigger>();
        }

        #region Actual Trigger Function Here
        [Function("APIUploadTrigger")]
        public void Run([TimerTrigger("%TriggerTime%")] TimerInfo myTimer)
        {
            ILogger log = _logger;
            SendLog(log, "APIUploadTrigger timer function executed at: " + DateTime.Now.ToString(), LogEventLevel.Information);
            if (!Utils.CheckConfiguration(log))         //  Make sure all required config items are present
                return;

            string? sConn = Environment.GetEnvironmentVariable("AzureBlobStorage");
            BlobContainerClient container;
            try
            {                                           //  Try connecting to the blob container
                string? sContainer = Environment.GetEnvironmentVariable("BlobContainer");
                if(string.IsNullOrEmpty(sContainer))
                {
                    SendLog(log, "Unable to load BlobContainer from configuration, quitting", LogEventLevel.Warning);
                    return;
                }
                container = new BlobContainerClient(sConn, sContainer);
            }
            catch (Exception e)
            {
                SendLog(log, "Exception connecting to blob container: " + e.ToString(), LogEventLevel.Error);
                return;
            }
                                                        //  So far so good....now get the routing info from DB
            List<APIRouting> routingTable = APIRouting.GetRoutingTable(log);
            if (routingTable.Count == 0)
            {                                           //  If no data (most likely no DB connection), complain
                SendLog(log, "There is no printer routing information in PrintEndpointT", LogEventLevel.Error);
            }
            else                                        //  Otherwise start file processing based on routes
            {
                List<Task> taskList = [];
                foreach (APIRouting route in routingTable)
                {                                       //  Get any files for this route
                    List<string> files = APIUploadBlobTF.Blobs.GetContainerFiles(log, container, route.PrinterAbbr + "/", 10000);
                    if (files.Count != 0)               //  And if there are any, route according to connection type
                    {
                        switch (route.ConnectionType)
                        {
                            case 1:                     //  SFTP connection (port 22 default)
                                SendLog(log, "Starting SFTP task", LogEventLevel.Information);
                                taskList.Add(Task.Factory.StartNew(() => FTP.StartFTPProcess(log, container, route, files)));
                                break;
                            case 2:                     //  Azure file share
                                SendLog(log, "Starting file share task", LogEventLevel.Information);
                                taskList.Add(Task.Factory.StartNew(() => FileShare.SendAllFilesViaFileShare(log, route, container, route.PrinterAbbr + "/", 10000)));
                                break;
                            case 4:                     //  FTP connection (port 21 default)
                                SendLog(log, "Starting FTP task", LogEventLevel.Information);
                                taskList.Add(Task.Factory.StartNew(() => FTP21.StartFTPProcess(log, container, route, files)));
                                break;
                            default:
                                SendLog(log, "Unknown ConnectionType " + route.ConnectionType.ToString() + " found", LogEventLevel.Warning);
                                break;
                        }
                    }
                    else
                    {                                   //  note if no files found (in case there should be some)
                        if (route.FailureNum != 0)
                            Errors.ResetFailureNum(log, route);
                        SendLog(log, "There are no files in the container for " + route.PrinterAbbr, LogEventLevel.Information);
                    }
                }
                Task.WaitAll(taskList.ToArray());       //  Wait for all tasks to complete
                SendLog(log, "APIUploadTrigger timer function completed at: " + DateTime.Now.ToString(), LogEventLevel.Information);
            }
        }
        #endregion


        #region SendLog
        public static void SendLog(ILogger log, string sText, LogEventLevel level)
        {
            if(Utils.GetBoolSettingsValue("CanLog", true))
            {
                if (level == LogEventLevel.Verbose || level == LogEventLevel.Information)
                    log?.LogInformation(sText);
                if (level == LogEventLevel.Warning)
                    log?.LogWarning(sText);
                if (level == LogEventLevel.Error)
                    log?.LogError(sText);
                if (level == LogEventLevel.Fatal)
                    log?.LogCritical(sText);
                if (level == LogEventLevel.Debug)
                    log?.LogDebug(sText);

                DDog.SLog(sText, level);
            }
        }
        #endregion
    }

    #region HealthClass
    public class HealthClass
    {
                                        //  This simply attempts a data connection as the function "health check"
        [Function("hc")]
        public static IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req, ILogger log)
        {
            string? sConn = Environment.GetEnvironmentVariable("PrintEndpointT");
            if (!string.IsNullOrEmpty(sConn))
            {
                try
                {
                    SqlConnection conn = new(sConn);
                    conn.Open();
                    conn.Dispose();
                }
                catch (Exception)
                {
                    return new StatusCodeResult(500);
                }
                return new StatusCodeResult(200);
            }
            return new StatusCodeResult(500);
        }
    }
    #endregion
}
