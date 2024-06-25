using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Serilog.Events;


namespace APIUploadBlobTF
{
    public class Utils
    {
        #region CheckConfiguration
        public static bool CheckConfiguration(ILogger log)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AzureBlobStorage")))
            {
                APIUploadTrigger.SendLog(log, "AzureBlobStorage is not in configuration", LogEventLevel.Error);
                return false;
            }
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BlobContainer")))
            {
                APIUploadTrigger.SendLog(log, "BlobContainer is not in configuration", LogEventLevel.Error);
                return false;
            }
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PrintEndpointT")))
            {
                APIUploadTrigger.SendLog(log, "PrintEndpointT is not in configuration", LogEventLevel.Error);
                return false;
            }
            string? sVar = Environment.GetEnvironmentVariable("MaxFailures");
            if (!string.IsNullOrEmpty(sVar))
            {
                if (!int.TryParse(sVar, out int iMaxFailures))
                {
                    APIUploadTrigger.SendLog(log, "MaxFailures is in configuration but is not a number", LogEventLevel.Error);
                    return false;
                }
                string? sFailureDelays = Environment.GetEnvironmentVariable("FailureDelays");
                if (!string.IsNullOrEmpty(sFailureDelays))
                {
                    string[] strArray = sFailureDelays.Split(',');
                    if (strArray.Length != iMaxFailures)
                    {
                        APIUploadTrigger.SendLog(log, "The number of failure delays does not match MaxFailures in configuration", LogEventLevel.Error);
                        return false;
                    }
                    foreach (string s in strArray)
                    {
                        if (!int.TryParse(s, out _))
                        {
                            APIUploadTrigger.SendLog(log, "FailureDelays is in configuration but " + s + " is not a number", LogEventLevel.Error);
                            return false;
                        }
                    }
                }
            }
            return true;
        }
        #endregion

        #region GetBoolSettingsValue
        public static bool GetBoolSettingsValue(string sSetting, bool bDefault)
        {
            string? sVal = Environment.GetEnvironmentVariable(sSetting);
            if (string.IsNullOrEmpty(sVal))
                sVal = "true";
            if (!bool.TryParse(sVal, out bool i))
                i = bDefault;
            return i;
        }
        #endregion

        #region WriteFileStatusToDB
        public static void WriteFileStatusToDB(ILogger log, List<FileStatus> filesStatus)
        {
            string? sConn = Environment.GetEnvironmentVariable("PrintEndpointT");
            try
            {
                SqlConnection conn = new(sConn);
                conn.Open();
                foreach (FileStatus fileStatus in filesStatus)
                {
                    SqlCommand cmd = new("insert into PrintFileTransmissionLog (FileName, DestinationName, DestinationPath, TransmissionDateTime, Success) values ('" +
                                         fileStatus.Filename + "','" + fileStatus.DestinationName + "','" + fileStatus.DestinationPath + "','" +
                                         fileStatus.DestinationDateTime.ToString() + "',1)", conn);
                    cmd.ExecuteNonQuery();
                    cmd.Dispose();
                }
                conn.Close();
                conn.Dispose();
            }
            catch (Exception e)
            {
                APIUploadTrigger.SendLog(log, "Exception: " + e.ToString(), LogEventLevel.Error);
            }
        }
        #endregion
    }
}
