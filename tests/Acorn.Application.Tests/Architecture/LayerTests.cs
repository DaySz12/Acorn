using System.Reflection;
using System.Runtime.CompilerServices;
using Acorn.Application.Controllers;
using Acorn.Application.ViewModels;
using Acorn.Core;
using NetArchTest.Rules;

namespace Acorn.Application.Tests.Architecture;

/// <summary>Cross-layer rules from SPEC 3.1 for the Model and Controller assemblies.</summary>
public class LayerTests
{
    private static readonly Assembly Core = typeof(VaultSession).Assembly;
    private static readonly Assembly Application = typeof(AppState).Assembly;

    private static readonly string[] UiAndPlatform =
    [
        "Microsoft.AspNetCore",
        "Photino",
        "System.Windows",
        "Microsoft.Win32",
        "TextCopy",
    ];

    private static readonly string[] Network =
    [
        "System.Net.Http",
        "System.Net.Sockets",
        "System.Net.WebClient",
        "System.Net.WebRequest",
        "System.Net.HttpWebRequest",
    ];

    private static IEnumerable<string> ReferencedAssemblies(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? "");

    [Fact]
    public void Core_does_not_reference_application_app_or_ui()
    {
        var references = ReferencedAssemblies(Core).ToList();

        Assert.DoesNotContain(references, r => r.StartsWith("Acorn.Application", StringComparison.Ordinal));
        Assert.DoesNotContain(references, r => r.StartsWith("Acorn.App", StringComparison.Ordinal));
        Assert.DoesNotContain(references, r => UiAndPlatform.Any(p => r.StartsWith(p, StringComparison.Ordinal)));
    }

    [Fact]
    public void Application_does_not_reference_app_or_ui()
    {
        var references = ReferencedAssemblies(Application).ToList();

        Assert.DoesNotContain(references, r => r == "Acorn.App");
        Assert.DoesNotContain(references, r => UiAndPlatform.Any(p => r.StartsWith(p, StringComparison.Ordinal)));
    }

    [Fact]
    public void Application_types_do_not_depend_on_ui_platform_interop_or_network_apis()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny([.. UiAndPlatform, .. Network, "System.Runtime.InteropServices.DllImportAttribute", "System.Runtime.InteropServices.LibraryImportAttribute"])
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypes?.Select(t => t.FullName) ?? []));
    }

    [Fact]
    public void Core_types_do_not_depend_on_ui_or_network_apis()
    {
        var result = Types.InAssembly(Core)
            .ShouldNot()
            .HaveDependencyOnAny([.. UiAndPlatform, .. Network])
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypes?.Select(t => t.FullName) ?? []));
    }

    [Fact]
    public void Controllers_take_dependencies_only_through_one_public_constructor_and_hold_no_static_state()
    {
        var controllers = Application.GetTypes()
            .Where(t => t.Namespace == typeof(VaultController).Namespace && t.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(controllers);

        foreach (var controller in controllers)
        {
            Assert.Single(controller.GetConstructors());
            var staticFields = controller
                .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => !f.IsLiteral && f.GetCustomAttribute<CompilerGeneratedAttribute>() is null);
            Assert.Empty(staticFields);
        }
    }

    [Fact]
    public void Entry_list_view_model_has_no_password_value()
    {
        var properties = typeof(EntryListItemViewModel).GetProperties().Select(p => p.Name).ToList();

        Assert.Contains(nameof(EntryListItemViewModel.HasPassword), properties);
        Assert.DoesNotContain(properties, p => p.Contains("Password", StringComparison.Ordinal) && p != nameof(EntryListItemViewModel.HasPassword));
        Assert.DoesNotContain(properties, p => p is "Value" or "Secret");
    }

    [Fact]
    public void Detail_view_models_carry_no_secret_values()
    {
        Assert.DoesNotContain(typeof(EntryDetailViewModel).GetProperties(), p => p.Name == "Password");
        Assert.DoesNotContain(typeof(EntryEditViewModel).GetProperties(), p => p.Name == "Password");
    }
}
