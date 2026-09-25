using System.Reflection;

namespace Test
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            Assembly.LoadFile(Assembly.GetExecutingAssembly().Location);
        }
    }
}