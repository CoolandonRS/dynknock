using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dynknock_Server;

[JsonSerializable(typeof(Hallway))]
internal partial class HallwayContext : JsonSerializerContext {
    static HallwayContext() {
        s_defaultOptions = new JsonSerializerOptions {
            ReadCommentHandling = JsonCommentHandling.Skip
        };
    }
}