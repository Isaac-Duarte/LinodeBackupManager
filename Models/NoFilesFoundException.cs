using System;

namespace LinodeBackupManager.Models
{
    public class NoFilesFoundException : Exception
    {
        public NoFilesFoundException()
        {
        }

        public NoFilesFoundException(string message)
            : base(message)
        {
        }

        public NoFilesFoundException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}