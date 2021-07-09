namespace LinodeBackupManager.Models
{
    public class GeneralConfig
    {
        public string[] Directories { get; set; }
        public string[] Ignores { get; set; }
        public int DaysAfterDelete { get; set; }
    }

    public class S3Config
    {
        public string ServiceUrl { get; set; }
        public string RegionEndpoint { get; set; }
        public string AccessKeyId { get; set; }
        public string AccessKey { get; set; }
        public string BucketName { get; set; }
    }

    public class Config
    {
        public GeneralConfig GeneralConfig { get; set; }
        public S3Config S3Config { get; set; }
    }
}