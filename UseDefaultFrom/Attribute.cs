namespace UseDefaultFrom;

[AttributeUsage(AttributeTargets.Property)]
public class UseDefaultFromAttribute<T> : Attribute
{
    public string Property { get; }

    public UseDefaultFromAttribute(string property) => Property = property;
}
