using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace APIUploadBlobTF
{
    public class Errors
    {
        #region HandleError
        public static void HandleError(ILogger log, APIRouting route)
        {
            if(log != null)
                log.LogInformation("Handling failure for " + route.PrinterAbbr);
            DDog.SLog("Handling failure for " + route.PrinterAbbr, (LogEventLevel)2);
            string? sMaxFailures = Environment.GetEnvironmentVariable("MaxFailures");
            if (!int.TryParse(sMaxFailures, out int iMaxFailures))
                iMaxFailures = 4;
            string? str = Environment.GetEnvironmentVariable("FailureDelays");
            if (string.IsNullOrEmpty(str))
                str = "5,15,30,60";
            string[] strArray = str.Split(',');
            int iOldMaxFailure = route.FailureNum;
            if (route.FailureNum < iMaxFailures)
                ++route.FailureNum;
            int index = route.FailureNum - 1;
            DateTime dateTime = DateTime.Now.AddMinutes((double)int.Parse(strArray[index]));
            route.LastFailureTime = new DateTime?(dateTime);
            string? sConn = Environment.GetEnvironmentVariable("PrintEndpointT");
            try
            {
                SqlConnection conn = new(sConn);
                conn.Open();
                SqlCommand cmd = new("update PrintEndpointT set FailureNum=" + route.FailureNum.ToString() + 
                                     ",LastFailureTime='" + route.LastFailureTime.ToString() + "' where PrinterAbbr='" + route.PrinterAbbr + "'", conn);
                cmd.ExecuteNonQuery();
                conn.Close();
                conn.Dispose();
            }
            catch (Exception e)
            {
                if(log != null)
                    APIUploadTrigger.SendLog(log, "Exception: " + e.ToString(), LogEventLevel.Error);
            }
            if (iOldMaxFailure + 1 == route.FailureNum && route.FailureNum == iMaxFailures)
            {
                if (log != null)
                    APIUploadTrigger.SendLog(log, "The maximum number of failures have been reached for " + route.PrinterAbbr, LogEventLevel.Warning);
            }
        }
        #endregion

        #region ResetFailureNum
        public static void ResetFailureNum(ILogger log, APIRouting route)
        {
            APIUploadTrigger.SendLog(log, "Resetting failure count for " + route.PrinterAbbr, LogEventLevel.Information);
            string? sConn = Environment.GetEnvironmentVariable("PrintEndpointT");
            try
            {
                SqlConnection conn = new(sConn);
                conn.Open();
                SqlCommand cmd = new("Update PrintEndpointT set FailureNum=0,LastFailureTime=NULL where PrinterAbbr='" + route.PrinterAbbr + "'", conn);
                cmd.ExecuteNonQuery();
                conn.Close();
                conn.Dispose();
            }
            catch (Exception ex)
            {
                APIUploadTrigger.SendLog(log, "Exception: " + ex.ToString(), LogEventLevel.Error);
            }
        }
        #endregion
    }
}
