using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Transfer;
using LinodeBackupManager.Models;
using LinodeBackupManager.Services;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace LinodeBackupManager
{
    class Program
    {
        private static Config config;
        private readonly static DateTime startDate = DateTime.Now;
        private readonly static string starteDateString = startDate.ToString("yyyy-MM-dd-HH-mm", CultureInfo.InvariantCulture);

        static async Task Main(string[] args)
        {
            // Create logger
            Directory.CreateDirectory("logs");

            Log.Logger = new LoggerConfiguration()
                .WriteTo.File($"logs/lbm-{starteDateString}.log")
                .WriteTo.Console()
                .CreateLogger();

            Log.Information($"Started backup {starteDateString}");

            // Find and bind the config
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", true, false);
            
            config = new Config();
            builder.Build().Bind(config);

            Log.Information("Found and bound config.");
            
            // Create Zip Backup
            Directory.CreateDirectory("temp");

            try
            {
                await S3Service.CreateBackupAsync(config, $"temp/{starteDateString}.zip", 9);
            }
            catch (KeyNotFoundException keyNotFound)
            {
                Log.Error($"Error while creating zip archive {keyNotFound.Message}");
            }
            catch (NoFilesFoundException noFiles)
            {
                Log.Error($"There are no files to zip check the config.");
            }
            catch (Exception e)
            {
                Log.Error($"Unhandled exception. {e.Message}");
            }

            // Upload archive
            try
            {
                await S3Service.MultiPartUploadAsync(config, $"temp/{starteDateString}.zip");
            }
            catch (KeyNotFoundException keyNotFound)
            {
                Log.Error($"Error while creating zip archive {keyNotFound.Message}");
            }

            // Check and delete old backups in bucket
            try
            {
                await S3Service.DeleteOldBackupsAsync(config);
            }
            catch (KeyNotFoundException keyNotFound)
            {
                Log.Error($"Error while creating zip archive {keyNotFound.Message}");
            }

            Log.Information("Done! Closing application");
        }
    }
}
