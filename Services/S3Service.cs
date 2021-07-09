using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using ICSharpCode.SharpZipLib.Zip;
using LinodeBackupManager.Models;
using Serilog;

namespace LinodeBackupManager.Services
{
    public class S3Service
    {
        private readonly static ILogger _log = Log.ForContext<S3Service>();

        private static IEnumerable<string> filterFiles(string path, params string[] exts) {
            return Directory
                            .GetFiles(path, "*", SearchOption.AllDirectories)
                            .Where(file => !exts.Any(x => file.EndsWith(x, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// Creates a backup (zip archive)
        /// </summary>
        /// <param name="config">Appsetting config</param>
        /// <param name="outputPath">Where the zip archive will be</param>
        /// <param name="compressionLevel">1-9 Zip compression level</param>
        public static async Task CreateBackupAsync(Config config, string outputPath, int compressionLevel)
        {
            // Generate S3 Config
            AmazonS3Config amazonS3Config = new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(config.S3Config.RegionEndpoint),
                ServiceURL = config.S3Config.ServiceUrl
            };

            using (IAmazonS3 client = new AmazonS3Client(config.S3Config.AccessKeyId, config.S3Config.AccessKey, amazonS3Config))
            using (TransferUtility fileTransferUtility = new TransferUtility(client))
            {
                // Check if S3 Bucket Exists
                if (!await client.DoesS3BucketExistAsync(config.S3Config.BucketName))
                    throw new KeyNotFoundException($"Bucket doesn't exist. ({config.S3Config.BucketName})");

                // Create list of files that need backup
                List<string> files = new List<string>();

                foreach (string directory in  config.GeneralConfig.Directories)
                    files.AddRange(filterFiles(directory, config.GeneralConfig.Ignores));

                // Dummy check
                if (files.Count <= 0)
                    throw new NoFilesFoundException("There are no files to upload check config");


                _log.Information($"Zipping {files.Count} files");

                // Create ZIP archive where files will be placed
                using (ZipOutputStream zipStream = new ZipOutputStream(File.Create($"{outputPath}")))
                {
                    // Compression level 1-9
                    zipStream.SetLevel(compressionLevel);

                    byte[] buffer = new byte[1048576];
                    int bytesRead;
                    int count = 0;

                    // Asynchronously write files to zip
                    foreach (string file in files)
                    {
                        count++;

                        ZipEntry entry = new ZipEntry(file);
                        entry.DateTime = DateTime.Now;
                        zipStream.PutNextEntry(entry);

                        using (FileStream fileStream = File.OpenRead(file))
                        {
                            _log.Information($"Zipping {file} ({count}/{files.Count})");

                            while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                await zipStream.WriteAsync(buffer, 0, bytesRead);
                            }
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Uploads a file using S3 multi-part
        /// </summary>
        /// <param name="config">Appsettings conifg</param>
        /// <param name="filePath">File wanting to be uploaded</param>
        public static async Task MultiPartUploadAsync(Config config, string filePath)
        {
            // Check if file exists
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Could not find file.", filePath);
            
            // Generate S3 Config
            AmazonS3Config amazonS3Config = new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(config.S3Config.RegionEndpoint),
                ServiceURL = config.S3Config.ServiceUrl
            };

            // Create upload
            using (IAmazonS3 client = new AmazonS3Client(config.S3Config.AccessKeyId, config.S3Config.AccessKey, amazonS3Config))
            using (TransferUtility fileTransferUtility = new TransferUtility(client))
            {
                // Check if Bucket exists
                if (!await client.DoesS3BucketExistAsync(config.S3Config.BucketName))
                    throw new KeyNotFoundException($"Bucket doesn't exist. ({config.S3Config.BucketName})");

                TransferUtilityUploadRequest uploadRequest = new TransferUtilityUploadRequest
                {
                    BucketName = config.S3Config.BucketName,
                    FilePath = filePath,
                    Key = Path.GetFileName(filePath)
                };

                uploadRequest.UploadProgressEvent += new EventHandler<UploadProgressArgs>(uploadRequest_UploadPartProgressEvent);

                await fileTransferUtility.UploadAsync(uploadRequest);
            }
        }

        // Log progress
        static int oldPercentage = -1;
        
        static void uploadRequest_UploadPartProgressEvent(object sender, UploadProgressArgs e)
        {
            if (oldPercentage != e.PercentDone)
            {
                oldPercentage = e.PercentDone;
                Log.Information($"Upload status of backup: {e.PercentDone}%");
            }
        }

        /// <summary>
        /// Deletes backups older than a configurable amount of days
        /// </summary>
        /// <param name="config">Appsetting config</param>
        public static async Task DeleteOldBackupsAsync(Config config)
        {
            // Get date
            DateTime currentTime = DateTime.Now;

            // Get S3 config
            AmazonS3Config amazonS3Config = new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(config.S3Config.RegionEndpoint),
                ServiceURL = config.S3Config.ServiceUrl
            };

            using (IAmazonS3 client = new AmazonS3Client(config.S3Config.AccessKeyId, config.S3Config.AccessKey, amazonS3Config))
            {
                // Check if bucket exists
                if (!await client.DoesS3BucketExistAsync(config.S3Config.BucketName))
                    throw new KeyNotFoundException($"Bucket doesn't exist. ({config.S3Config.BucketName})");
                
                // Query and delete any buckets that are over x amount of days
                ListObjectsResponse response = await client.ListObjectsAsync(config.S3Config.BucketName);

                foreach (S3Object obj in response.S3Objects)
                {
                    if (currentTime.Subtract(obj.LastModified).Days >= config.GeneralConfig.DaysAfterDelete)
                    {
                        Log.Information($"Deleteing object {obj.Key} in bucket {obj.BucketName}");
                        await client.DeleteObjectAsync(obj.BucketName, obj.Key);
                    }
                }
            }
        }
    }
}