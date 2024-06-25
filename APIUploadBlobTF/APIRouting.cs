using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;


namespace APIUploadBlobTF
{
    public class APIRouting
    {                                           //  routing class data
        public int ConnectionType { set; get; }

        public string? PrinterAbbr { get; set; }

        public int ID { get; set; }

        public string? URL { get; set; }

        public int Port { get; set; }

        public string? Path { get; set; }

        public string? Username { get; set; }

        public string? Password { get; set; }

        public string? Headers { get; set; }

        public bool AzureFileShare { get; set; }

        public int Priority { get; set; }

        public int MaxConnections { get; set; }

        public int FailureNum { get; set; }

        public DateTime? LastFailureTime { get; set; }


        #region GetRoutingTable
        public static List<APIRouting> GetRoutingTable(ILogger log)
        {                                       //  Get data for all active routes
            string? sConn = Environment.GetEnvironmentVariable("PrintEndpointT");
            SqlConnection? conn = null;
            SqlCommand? cmd = null;
            SqlDataReader? reader = null;
            List<APIRouting> routingTable = [];
            try
            {
                conn = new(sConn);
                conn.Open();
                cmd = new("select ConnectionType, URL, Port, Path, Username, Password, Headers, PrinterID, AzureFileShare, PrinterAbbr, " +
                          "Priority, MaxConnections, FailureNum, LastFailureTime from PrintEndpointT where IsActive=1 order by Priority asc", conn);
                reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    routingTable.Add(new APIRouting()
                    {
                        ConnectionType = reader.GetInt32(0),
                        URL = reader.IsDBNull(1) ? null : reader.GetString(1),
                        Port = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                        Path = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Username = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Password = reader.IsDBNull(5) ? null : reader.GetString(5),
                        Headers = reader.IsDBNull(6) ? null : reader.GetString(6),
                        ID = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                        AzureFileShare = reader.IsDBNull(8) || reader.GetBoolean(8),
                        PrinterAbbr = reader.GetString(9),
                        Priority = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                        MaxConnections = reader.IsDBNull(11) ? 1 : reader.GetInt32(11),
                        FailureNum = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
                        LastFailureTime = reader.IsDBNull(13) ? new DateTime?() : reader.GetDateTime(13)
                    });
                }
                reader.Close();
                cmd.Dispose();
                conn.Close();
                conn.Dispose();
            }
            catch (Exception e)
            {
                if (reader != null && !reader.IsClosed)
                    reader.Close();
                if (cmd != null)
                    cmd.Dispose();
                if (conn != null && conn.State != System.Data.ConnectionState.Closed)
                {
                    conn.Close();
                    conn.Dispose();
                }
                APIUploadTrigger.SendLog(log, "Exception retrieving data: " + e.ToString(), Serilog.Events.LogEventLevel.Error);
            }
            return routingTable;
        }
        #endregion


        #region GetHeaderArrayFromHeaders
        //  Split comma delimited key/value pairs and then semicolon delimited data into a list of key/value pairs
        public static Dictionary<string, string> GetHeaderArrayFromHeaders(string sHeaders)
        {
            Dictionary<string, string> arrayFromHeaders = [];
            string[] strArray1 = sHeaders.Split(',');
            if (strArray1.Length != 0)
            {
                foreach (string str in strArray1)
                {
                    string[] strArray2 = str.Split(';');
                    if (strArray2.Length == 2)
                        arrayFromHeaders.Add(strArray2[0], strArray2[1]);
                }
            }
            return arrayFromHeaders;
        }
        #endregion
    }
}
