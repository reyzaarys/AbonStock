using Xunit.Abstractions;
using Xunit.Sdk;

namespace AbonStock.Tests.Common;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class TestPriorityAttribute(int priority) : Attribute
{
    public int Priority { get; } = priority;
}

public class PriorityOrderer : ITestCaseOrderer
{
    public const string TypeName = "AbonStock.Tests.Common.PriorityOrderer";
    public const string AssemblyName = "AbonStock.Tests";

    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
    {
        return testCases.OrderBy(tc =>
        {
            var attr = tc.TestMethod.Method
                .GetCustomAttributes(typeof(TestPriorityAttribute).AssemblyQualifiedName)
                .FirstOrDefault();
            return attr?.GetNamedArgument<int>(nameof(TestPriorityAttribute.Priority)) ?? 0;
        });
    }
}
