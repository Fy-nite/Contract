using System;
[AttributeUsage(AttributeTargets.Class)]
public class MyAttrAttribute : Attribute {
  public MyAttrAttribute() {}
  public MyAttrAttribute(string name) {Name=name;}
  public MyAttrAttribute(string name, int value) {Name=name; Value=value;}
  public string? Name;
  public int Value;
}
[AttributeUsage(AttributeTargets.Class)]
public class GenericAttrAttribute<T> : Attribute {
  public GenericAttrAttribute(T value) {Value=value;}
  public T Value;
}
