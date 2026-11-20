using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

class InspectClient
{
    static int Main(string[] paths)
    {
        foreach (var path in paths)
        {
            using var input = File.OpenRead(path);
            using var pe = new PEReader(input);
            var reader = pe.GetMetadataReader();
            var records = new List<object>();
            var types = new List<object>();
            foreach (var handle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(handle);
                var name = reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
                if (name.EndsWith(".App") || name.Contains("Update", StringComparison.OrdinalIgnoreCase) || name.Contains("Updata", StringComparison.OrdinalIgnoreCase) || name.Contains("Exam", StringComparison.OrdinalIgnoreCase))
                    types.Add(new { name, attributes = type.Attributes.ToString(), fields = type.GetFields().Select(h => reader.GetString(reader.GetFieldDefinition(h).Name)).ToArray(), methods = type.GetMethods().Select(h => reader.GetString(reader.GetMethodDefinition(h).Name)).ToArray() });
                foreach (var methodHandle in type.GetMethods())
                {
                    var method = reader.GetMethodDefinition(methodHandle);
                    if (method.RelativeVirtualAddress == 0) continue;
                    var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
                    var strings = new SortedSet<string>();
                    for (var i = 0; i + 4 < il.Length; i++)
                    {
                        if (il[i] != 0x72 || il[i + 4] != 0x70) continue;
                        var token = BitConverter.ToInt32(il, i + 1) & 0x00ffffff;
                        try
                        {
                            var value = reader.GetUserString(MetadataTokens.UserStringHandle(token));
                            if (value.Length <= 512 && new[] { "http", ".xml", ".dll", ".exe", ".lab", "update", "updata", "version" }.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase))) strings.Add(value);
                        }
                        catch (BadImageFormatException) { }
                    }
                    if (strings.Count > 0) records.Add(new { method = name + "." + reader.GetString(method.Name), strings });
                }
            }
            Console.WriteLine(JsonSerializer.Serialize(new { file = Path.GetFileName(path), types, strings = records }));
        }
        return 0;
    }
}
