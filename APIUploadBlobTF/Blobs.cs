using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs;
using Azure;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace APIUploadBlobTF
{
    public class Blobs
    {
        #region GetContainerFiles
        public static List<string> GetContainerFiles(ILogger log, BlobContainerClient container, string prefix, int iSegmentSize)
        {
            IEnumerable<Page<BlobHierarchyItem>> pages = container.GetBlobsByHierarchy(delimiter: "/", prefix: prefix).AsPages(pageSizeHint: new int?(iSegmentSize));
            List<string> containerFiles = [];
            foreach (Page<BlobHierarchyItem> page in pages)
            {
                foreach (BlobHierarchyItem blobHierarchyItem in (IEnumerable<BlobHierarchyItem>)page.Values)
                {
                    if (!blobHierarchyItem.IsPrefix)
                    {
                        string str = blobHierarchyItem.Blob.Name;
                        if (str.LastIndexOf('/') != -1)
                            str = str.Substring(str.LastIndexOf('/') + 1);
                        containerFiles.Add(str);
                    }
                }
            }
            return containerFiles;
        }
        #endregion GetContainerFiles

        #region ReadBlobData
        public static byte[] ReadBlobData(ILogger log, BlobContainerClient container, string sFilename)
        {
            try
            {
                return container.GetBlobClient(sFilename).DownloadContent().Value.Content.ToArray();
            }
            catch (Exception ex)
            {
                APIUploadTrigger.SendLog(log, "Exception in ReadBlobData: " + ex.ToString(), LogEventLevel.Error);
                return [];
            }
        }
        #endregion

        #region DeleteBlob
        public static void DeleteBlob(ILogger log, BlobContainerClient container, string sFilename)
        {
            try
            {
                container.GetBlobClient(sFilename).DeleteIfExists();
            }
            catch (Exception ex)
            {
                APIUploadTrigger.SendLog(log, "Exception in DeleteBlob: " + ex.ToString(), LogEventLevel.Error);
            }
        }
        #endregion
    }
}
