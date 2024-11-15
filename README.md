Reuse default property values from other classes.

Relies on .NET 9, which added support for partial property declarations.

```
public class Foo
{
    public string Hello { get; set; } = "World";
}

public partial class Bar
{
    [UseDefaultFrom<Foo>(nameof(Foo.Hello))]
    public partial string Hello { get; set; }
}
```

In this example, the default value of Bar.Hello will now be "World". This allows you to no longer have to duplicate default declarations.

Unfortunately, generic type parameters are not supported. https://github.com/dotnet/csharplang/discussions/7252

Given the following class:

```
public class Foo<T> where T : new()
{
    public string Hello { get; set; } = "World";

    public T Data { get; set; } = new T();
}
```

Does not work:

```
public partial class Bar<T> where T : new()
{
    [UseDefaultFrom<Foo<T>>(nameof(Foo.Hello))]
    public partial string Hello { get; set; }

    [UseDefaultFrom<Foo<T>>(nameof(Foo.Hello))]
    public partial T Data { get; set; }
}
```

Does work:

```
public partial class Bar<T> where T : new()
{
    [UseDefaultFrom<Foo<object>>(nameof(Foo.Hello))]
    public partial string Hello { get; set; }

    public T Data { get; set; } = new T();
}
```

This was originally created to make creating generic Blazor components more convenient, where you often have re-exposed passthrough parameters. However, it can be used in any context.
