using System;
using System.IO;
using System.Linq;
static class OfflineModelTests {
 static int Main(string[] args){try{if(args.Length!=2)throw new ArgumentException("model, expected packed weights");var m=NamModel.Load(args[0]);if(!NamModel.Bytes(m.Weights).SequenceEqual(File.ReadAllBytes(args[1])))throw new Exception("Original container import differs");Console.WriteLine("ORIGINAL_CONTAINER_PASS "+m.SourceHash);return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
