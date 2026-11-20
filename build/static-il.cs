using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;

sealed class Sig : ISignatureTypeProvider<string, object>
{
    public string GetArrayType(string t, ArrayShape s) => t + "[" + new string(',', s.Rank-1) + "]";
    public string GetByReferenceType(string t) => t+"&";
    public string GetFunctionPointerType(MethodSignature<string> s) => "methodptr";
    public string GetGenericInstantiation(string t, ImmutableArray<string> a) => t+"<"+string.Join(",",a)+">";
    public string GetGenericMethodParameter(object c, int n) => "!!"+n;
    public string GetGenericTypeParameter(object c, int n) => "!"+n;
    public string GetModifiedType(string m,string t,bool r) => t;
    public string GetPinnedType(string t) => t+" pinned";
    public string GetPointerType(string t) => t+"*";
    public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString();
    public string GetSZArrayType(string t) => t+"[]";
    public string GetTypeFromDefinition(MetadataReader r,TypeDefinitionHandle h,byte k) { var t=r.GetTypeDefinition(h);return r.GetString(t.Namespace)+"."+r.GetString(t.Name); }
    public string GetTypeFromReference(MetadataReader r,TypeReferenceHandle h,byte k) { var t=r.GetTypeReference(h);return r.GetString(t.Namespace)+"."+r.GetString(t.Name); }
    public string GetTypeFromSpecification(MetadataReader r,object c,TypeSpecificationHandle h,byte k) => r.GetTypeSpecification(h).DecodeSignature(this,c);
}
class StaticIL
{
    static MetadataReader r;
    static Sig sig = new Sig();
    static Dictionary<int, string> owners = new Dictionary<int,string>();
    static Dictionary<ushort,OpCode> ops = typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)).ToDictionary(o=>unchecked((ushort)o.Value));
    static string Signature(MethodSignature<string> s) => "("+string.Join(", ",s.ParameterTypes)+") -> "+s.ReturnType;
    static string Entity(EntityHandle h)
    {
        try {
            switch(h.Kind) {
                case HandleKind.TypeDefinition: return sig.GetTypeFromDefinition(r,(TypeDefinitionHandle)h,0);
                case HandleKind.TypeReference: return sig.GetTypeFromReference(r,(TypeReferenceHandle)h,0);
                case HandleKind.TypeSpecification: return r.GetTypeSpecification((TypeSpecificationHandle)h).DecodeSignature(sig,null);
                case HandleKind.MethodDefinition: var m=r.GetMethodDefinition((MethodDefinitionHandle)h);return owners.GetValueOrDefault(MetadataTokens.GetToken(h),"?")+"."+r.GetString(m.Name)+Signature(m.DecodeSignature(sig,null));
                case HandleKind.FieldDefinition: var f=r.GetFieldDefinition((FieldDefinitionHandle)h);return owners.GetValueOrDefault(MetadataTokens.GetToken(h),"?")+"."+r.GetString(f.Name)+" : "+f.DecodeSignature(sig,null);
                case HandleKind.MemberReference: var mr=r.GetMemberReference((MemberReferenceHandle)h);return Entity(mr.Parent)+"."+r.GetString(mr.Name)+(mr.GetKind()==MemberReferenceKind.Method?Signature(mr.DecodeMethodSignature(sig,null)):" : "+mr.DecodeFieldSignature(sig,null));
                case HandleKind.MethodSpecification: var ms=r.GetMethodSpecification((MethodSpecificationHandle)h);return Entity(ms.Method)+"<"+string.Join(",",ms.DecodeSignature(sig,null))+">";
                case HandleKind.StandaloneSignature: return "signature#"+MetadataTokens.GetRowNumber(h);
                default: return h.Kind+"#"+MetadataTokens.GetRowNumber(h);
            }
        } catch(Exception e) { return h.Kind+"#"+MetadataTokens.GetToken(h).ToString("X8")+"("+e.GetType().Name+")"; }
    }
    static int Main(string[] args)
    {
        if(args.Length<1) return 2;
        using var input=File.OpenRead(args[0]);using var pe=new PEReader(input);r=pe.GetMetadataReader();
        var filter=new Regex(args.Length>1?args[1]:".*",RegexOptions.IgnoreCase);
        foreach(var th in r.TypeDefinitions){var t=r.GetTypeDefinition(th);string name=Entity(th);foreach(var h in t.GetMethods())owners[MetadataTokens.GetToken(h)]=name;foreach(var h in t.GetFields())owners[MetadataTokens.GetToken(h)]=name;}
        var asm=r.GetAssemblyDefinition();Console.WriteLine("ASSEMBLY "+r.GetString(asm.Name)+" "+asm.Version);
        foreach(var th in r.TypeDefinitions){var t=r.GetTypeDefinition(th);string type=Entity(th);bool mt=filter.IsMatch(type);
            if(mt){Console.WriteLine("TYPE "+type+" : "+Entity(t.BaseType));foreach(var h in t.GetFields())Console.WriteLine("FIELD "+Entity(h));foreach(var h in t.GetProperties()){var p=r.GetPropertyDefinition(h);Console.WriteLine("PROPERTY "+r.GetString(p.Name)+Signature(p.DecodeSignature(sig,null)));}}
            foreach(var h in t.GetMethods()) {
                var m=r.GetMethodDefinition(h);string name=Entity(h);if(!mt&&!filter.IsMatch(name))continue;
                Console.WriteLine("METHOD 0x"+MetadataTokens.GetToken(h).ToString("X8")+" RVA="+m.RelativeVirtualAddress+" "+name);
                if(m.RelativeVirtualAddress==0)continue;
                var body=pe.GetMethodBody(m.RelativeVirtualAddress);var b=body.GetILBytes();int p=0;
                while(p<b.Length){int start=p;ushort key=b[p++];if(key==0xfe)key=(ushort)(0xfe00|b[p++]);if(!ops.TryGetValue(key,out var op))throw new InvalidDataException("Unknown opcode "+key.ToString("X4"));string operand="";
                    switch(op.OperandType){
                        case OperandType.InlineNone:break;
                        case OperandType.ShortInlineI:operand=unchecked((sbyte)b[p++]).ToString();break;
                        case OperandType.InlineI:operand=BitConverter.ToInt32(b,p).ToString();p+=4;break;
                        case OperandType.InlineI8:operand=BitConverter.ToInt64(b,p).ToString();p+=8;break;
                        case OperandType.ShortInlineR:operand=BitConverter.ToSingle(b,p).ToString(System.Globalization.CultureInfo.InvariantCulture);p+=4;break;
                        case OperandType.InlineR:operand=BitConverter.ToDouble(b,p).ToString(System.Globalization.CultureInfo.InvariantCulture);p+=8;break;
                        case OperandType.ShortInlineVar:operand=b[p++].ToString();break;
                        case OperandType.InlineVar:operand=BitConverter.ToUInt16(b,p).ToString();p+=2;break;
                        case OperandType.ShortInlineBrTarget:int d=unchecked((sbyte)b[p++]);operand="IL_"+(p+d).ToString("X4");break;
                        case OperandType.InlineBrTarget:int delta=BitConverter.ToInt32(b,p);p+=4;operand="IL_"+(p+delta).ToString("X4");break;
                        case OperandType.InlineSwitch:int n=BitConverter.ToInt32(b,p);p+=4;int end=p+4*n;var targets=new List<string>();for(int j=0;j<n;j++){targets.Add("IL_"+(end+BitConverter.ToInt32(b,p)).ToString("X4"));p+=4;}operand=string.Join(", ",targets);break;
                        case OperandType.InlineString:int s=BitConverter.ToInt32(b,p);p+=4;operand=JsonSerializer.Serialize(r.GetUserString(MetadataTokens.UserStringHandle(s&0xffffff)));break;
                        default:int token=BitConverter.ToInt32(b,p);p+=4;operand="0x"+token.ToString("X8")+" "+Entity(MetadataTokens.EntityHandle(token));break;
                    }
                    Console.WriteLine("IL_"+start.ToString("X4")+": "+op.Name+" "+operand);
                }
                foreach(var eh in body.ExceptionRegions)Console.WriteLine("EXCEPTION "+eh.Kind+" try="+eh.TryOffset+"+"+eh.TryLength+" handler="+eh.HandlerOffset+"+"+eh.HandlerLength+" catch="+Entity(eh.CatchType));
            }
        }
        return 0;
    }
}
