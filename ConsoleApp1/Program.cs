// See https://aka.ms/new-console-template for more information

using System.Reflection;

Console.WriteLine("Hello, World!");

var asm = Assembly.LoadFile("/Users/jensschulze/workspace/SWCUOPlugin/SWCUOPlugin/bin/Release/net8.0/SWCUOPlugin.dll");


foreach (var type in asm.GetTypes())
{
    Console.WriteLine(type);    
}

Console.WriteLine(asm.GetType("Assistant.Engine"));