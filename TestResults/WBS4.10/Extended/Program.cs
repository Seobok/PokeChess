using System;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework.Api;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
class Program {
 static int Main(){var r=new NUnitTestAssemblyRunner(new DefaultTestAssemblyBuilder());r.Load(Assembly.GetExecutingAssembly(),new Dictionary<string,object>());var result=r.Run(TestListener.NULL,TestFilter.Empty);Console.WriteLine(result.ToXml(true).OuterXml);return result.FailCount==0?0:1;}
}
