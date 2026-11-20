using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

class InspectAssembly
{
    static string TypeName(MetadataReader reader, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeReference)
        {
            var type = reader.GetTypeReference((TypeReferenceHandle)handle);
            return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
        }
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            var type = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
            return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
        }
        return handle.Kind.ToString();
    }
    static int Main(string[] paths)
    {
        foreach (var path in paths)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var pe = new PEReader(stream);
                if (!pe.HasMetadata)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { file = Path.GetFileName(path), managed = false }));
                    continue;
                }
                var reader = pe.GetMetadataReader();
                if (!reader.IsAssembly)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { file = Path.GetFileName(path), managed = true, assembly = false }));
                    continue;
                }
                var assembly = reader.GetAssemblyDefinition();
                string framework = null;
                foreach (var handle in assembly.GetCustomAttributes())
                {
                    var attribute = reader.GetCustomAttribute(handle);
                    if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
                    var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                    if (TypeName(reader, member.Parent) != "System.Runtime.Versioning.TargetFrameworkAttribute") continue;
                    var blob = reader.GetBlobReader(attribute.Value);
                    if (blob.ReadUInt16() == 1) framework = blob.ReadSerializedString();
                }
                string entryPoint = null;
                int token = pe.PEHeaders.CorHeader.EntryPointTokenOrRelativeVirtualAddress;
                if ((token >> 24) == 0x06 && (token & 0xffffff) != 0)
                {
                    var method = reader.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.Handle(token));
                    entryPoint = TypeName(reader, method.GetDeclaringType()) + "." + reader.GetString(method.Name);
                }
                var references = reader.AssemblyReferences.Select(handle => {
                    var reference = reader.GetAssemblyReference(handle);
                    return new { name = reader.GetString(reference.Name), version = reference.Version.ToString(), publicKeyOrToken = Convert.ToHexString(reader.GetBlobBytes(reference.PublicKeyOrToken)).ToLowerInvariant() };
                }).ToArray();
                var notableTypes = reader.TypeDefinitions.Select(handle => TypeName(reader, handle)).Where(name => name.EndsWith(".App") || name.EndsWith(".RunningInfo") || name.EndsWith(".ExamStartInfo") || name.EndsWith(".Package") || name.EndsWith(".JsonReader") || name.EndsWith(".JsonWriter")).ToArray();
                var result = new { file = Path.GetFileName(path), managed = true, assembly = true, name = reader.GetString(assembly.Name), version = assembly.Version.ToString(), framework, entryPoint, machine = pe.PEHeaders.CoffHeader.Machine.ToString(), clrFlags = pe.PEHeaders.CorHeader.Flags.ToString(), hasNativeResources = pe.PEHeaders.PEHeader.ResourceTableDirectory.Size > 0, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), references, notableTypes };
                Console.WriteLine(JsonSerializer.Serialize(result));
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(Path.GetFileName(path) + ": " + error.Message);
                return 1;
            }
        }
        return 0;
    }
}
