using MessagePack;
using MessagePack.Resolvers;
using System.IO;

namespace SignalRServer
{
    public class TypelessMessage
    {
        public string From { get; set; } = "Server";
        public string To { get; set; } = "All";
        public string Command { get; set; } = "Update";     // PowerOn, PowerOff, Update, etc.
        public string DataType { get; set; } = "String";    // Type Name (e.g., System.String, System.Int32, MyNamespace.MyClass, etc.)
        public object Data { get; set; } = "";              // Typed Data
    }

    public class StateMessage
    {
        public string Who { get; set; } = "Unknown";            // e.g., User Name or Device ID
        public string State { get; set; } = "Disconnected";     // e.g., Online, Offline, Busy, etc.
        public string Description { get; set; } = "";           // Optional description
    }

    public class TypelessMessageHelper
    {
        public static async Task<byte[]> SerializeAsync(TypelessMessage msg)
        {
            var _options = MessagePackSerializerOptions.Standard
                .WithResolver(TypelessContractlessStandardResolver.Instance);

            using var ms = new MemoryStream();

            await MessagePackSerializer.SerializeAsync(ms, msg, _options);

            return ms.ToArray();
        }

        public static async Task<TypelessMessage> DeserializeAsync(byte[] class_data)
        {
            var options = MessagePackSerializerOptions.Standard
                .WithResolver(TypelessContractlessStandardResolver.Instance);

            using var ms = new MemoryStream(class_data);

            return await MessagePackSerializer.DeserializeAsync<TypelessMessage>(ms, options);
        }
    }
}
