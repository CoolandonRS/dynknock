using System.Text.Json.Serialization;

namespace Dynknock_Client; 

public class Hallway {
    // internal set not because desired, but because source generation requires public or internal set, and if I want to trim assemblies I need to use source generation, and internal is better then public.
    // ReSharper disable InconsistentNaming
    [JsonInclude]
    public string hostname { get; [Obsolete("Use only for source generation")] internal set; }
    [JsonInclude]
    public string key { get; [Obsolete("Use only for source generation")] internal set; }
    [JsonInclude]
    public int interval { get; [Obsolete("Use only for source generation")] internal set; }
    [JsonInclude]
    public int length { get; [Obsolete("Use only for source generation")] internal set; }
    [JsonInclude]
    public int timeout { get; [Obsolete("Use only for source generation")] internal set; }
    [JsonInclude]
    public int doorbell { get; [Obsolete("Use only for source generation")] internal set; }
    [JsonInclude] 
    public int pause { get; [Obsolete("Use only for source generation")] internal set; }
    // ReSharper restore InconsistentNaming
}