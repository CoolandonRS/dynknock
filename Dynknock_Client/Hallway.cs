using System.Text.Json.Serialization;

namespace Dynknock_Client; 

public class Hallway {
    [JsonInclude] 
    public readonly string hostname;
    [JsonInclude]
    public readonly string key;
    [JsonInclude]
    public readonly int interval;
    [JsonInclude]
    public readonly int length;
    [JsonInclude]
    public readonly int timeout;
    [JsonInclude]
    public readonly int doorbell;
    [JsonInclude] 
    public readonly int pause;
}