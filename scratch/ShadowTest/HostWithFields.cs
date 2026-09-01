using System;
using ObjektRT.Core.Attributes;
[ClassBinding("HostBox")]
public class HostBox {
  public int value;
  public string label = "hostLabel";
  public string Name { get; set; } = "propName";
  [MethodBinding] public static void Print(string s) => Console.WriteLine("HostBox.Print:" + s);
  [MethodBinding] public void SetValue(int v) => value = v;
  [MethodBinding] public int GetValue() => value;
}
