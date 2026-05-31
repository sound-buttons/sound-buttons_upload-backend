using System;
using System.Collections.Generic;
using System.Threading;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SoundButtons.Tests.Fakes;

/// <summary>
///     Minimal <see cref="FunctionContext" /> double. The only member exercised by the
///     code under test is <see cref="InstanceServices" />, which must expose the worker
///     serializer so <c>WriteAsJsonAsync</c> (used by the Durable check-status response)
///     succeeds.
/// </summary>
public sealed class TestFunctionContext : FunctionContext
{
    public TestFunctionContext()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<WorkerOptions>(o => o.Serializer = new JsonObjectSerializer());
        InstanceServices = services.BuildServiceProvider();
    }

    public override IServiceProvider InstanceServices { get; set; }

    public override string InvocationId => "test-invocation";

    public override string FunctionId => "test-function";

    public override TraceContext TraceContext => null!;

    public override BindingContext BindingContext => null!;

    public override RetryContext RetryContext => null!;

    public override FunctionDefinition FunctionDefinition => null!;

    public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();

    public override IInvocationFeatures Features => null!;

    public override CancellationToken CancellationToken => CancellationToken.None;
}
