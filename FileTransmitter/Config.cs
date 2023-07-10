using System;
using System.Text.Json.Serialization;
using System.Windows;

namespace FileTransmitter
{
    
    public class Config
    {
        [JsonInclude] public string DirectoryForSave { get; set; }
        [JsonInclude] public int Port { get; set; }
        [JsonInclude] public string IpAddress { get; set; }

        public Config() 
        {

        }
    }
}
