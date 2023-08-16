
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dynknock_Client;

#pragma warning disable CS0619
[JsonSerializable(typeof(Hallway))]
internal partial class HallwayContext : JsonSerializerContext {
   static HallwayContext() {
      s_defaultOptions = new JsonSerializerOptions {
         ReadCommentHandling = JsonCommentHandling.Skip
      };
   }
}