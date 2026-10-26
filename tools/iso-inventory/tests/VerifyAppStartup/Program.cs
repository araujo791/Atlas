using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: VerifyAppStartup <managed Windows app DLL>");
    return 2;
}

using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var token = pe.PEHeaders.CorHeader?.EntryPointTokenOrRelativeVirtualAddress ?? 0;
if (token == 0) throw new InvalidOperationException("Application has no managed entry point.");
var metadata = pe.GetMetadataReader();
var handle = MetadataTokens.Handle(token);
if (handle.Kind != HandleKind.MethodDefinition)
    throw new InvalidOperationException("Unexpected application entry point.");
var main = metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
var hasSta = main.GetCustomAttributes().Any(handle =>
{
    var attribute = metadata.GetCustomAttribute(handle);
    if (attribute.Constructor.Kind != HandleKind.MemberReference) return false;
    var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
    if (constructor.Parent.Kind != HandleKind.TypeReference) return false;
    var type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
    return metadata.GetString(type.Namespace) == "System" &&
        metadata.GetString(type.Name) == "STAThreadAttribute";
});
if (!hasSta)
{
    Console.Error.WriteLine("Windows UI entry point is missing STAThread; native file/folder dialogs are unsafe.");
    return 1;
}
Console.WriteLine("PASS: compiled application entry point " + metadata.GetString(main.Name) + " has STAThread.");
return 0;
