using System.Reflection;
using Acorn.App.Views;
using Acorn.Application.Controllers;
using Microsoft.AspNetCore.Components;
using NetArchTest.Rules;

namespace Acorn.Application.Tests.Architecture;

/// <summary>
/// SPEC 3.1 View rules, checked against the compiled Razor components: views talk to controllers
/// and AppState only, and never touch the Model, platform services, crypto, files or the network.
/// </summary>
public class ViewLayerTests
{
    private static readonly Assembly AppAssembly = typeof(AppStateView).Assembly;
    private static readonly string[] ViewNamespaces = ["Acorn.App.Views", "Acorn.App.Components"];

    private static IEnumerable<Type> ViewTypes() => AppAssembly.GetTypes()
        .Where(t => ViewNamespaces.Contains(t.Namespace) && typeof(IComponent).IsAssignableFrom(t));

    [Fact]
    public void Views_and_components_inject_only_controllers_and_app_state()
    {
        var views = ViewTypes().ToList();
        Assert.NotEmpty(views);

        foreach (var view in views)
        {
            for (var type = view; type is not null && type != typeof(ComponentBase); type = type.BaseType)
            {
                var injected = type
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(p => p.GetCustomAttribute<InjectAttribute>() is not null);
                foreach (var property in injected)
                {
                    var allowed = property.PropertyType == typeof(AppState)
                        || property.PropertyType.Namespace == typeof(VaultController).Namespace;
                    Assert.True(allowed, $"{view.Name} injects {property.PropertyType.FullName}");
                }
            }
        }
    }

    [Fact]
    public void Views_do_not_depend_on_model_services_platform_crypto_io_or_network()
    {
        // Guard against a vacuous pass if the namespace filter ever matches nothing.
        var selected = Types.InAssembly(AppAssembly)
            .That().ResideInNamespace(ViewNamespaces[0]).Or().ResideInNamespace(ViewNamespaces[1])
            .GetTypes();
        Assert.True(selected.Count() >= 10);

        var result = Types.InAssembly(AppAssembly)
            .That().ResideInNamespace(ViewNamespaces[0]).Or().ResideInNamespace(ViewNamespaces[1])
            .ShouldNot().HaveDependencyOnAny(
                "Acorn.Core",
                "Acorn.Application.Abstractions",
                "Acorn.Application.Services",
                "Acorn.App.Platform",
                "Photino",
                "TextCopy",
                "Microsoft.JSInterop",
                "System.IO",
                "System.Net",
                "System.Security.Cryptography")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypes?.Select(t => t.FullName) ?? []));
    }

    [Fact]
    public void Views_unsubscribe_from_app_state_by_implementing_IDisposable()
    {
        var subscribers = ViewTypes().Where(t => typeof(AppStateView).IsAssignableFrom(t)).ToList();

        Assert.NotEmpty(subscribers);
        Assert.All(subscribers, t => Assert.True(typeof(IDisposable).IsAssignableFrom(t), t.Name));
    }
}
