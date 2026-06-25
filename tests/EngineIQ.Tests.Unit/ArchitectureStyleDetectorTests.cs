using EngineIQ.ContextBuilder.Architecture;
using EngineIQ.Domain.Context;

namespace EngineIQ.Tests.Unit;

public class ArchitectureStyleDetectorTests
{
    [Fact]
    public void Detect_clean_architecture_from_typical_dotnet_folders()
    {
        var paths = new[]
        {
            "src/Acme.Domain/Entities/Order.cs",
            "src/Acme.Application/Orders/CreateOrderHandler.cs",
            "src/Acme.Infrastructure/Persistence/OrderRepository.cs",
            "src/Acme.API/Controllers/OrdersController.cs",
        };

        var context = ArchitectureStyleDetector.Detect(paths);

        Assert.Equal(ArchitectureStyles.Clean, context.DetectedStyle);
        Assert.True(context.LayerFolderMap.ContainsKey("Domain"));
        Assert.Contains("src/Acme.Domain", context.LayerFolderMap["Domain"]);
        Assert.Contains("src/Acme.Infrastructure", context.LayerFolderMap["Infrastructure"]);
    }

    [Fact]
    public void Detect_layered_architecture_from_three_tier_folders()
    {
        var paths = new[]
        {
            "src/MyApp.Presentation/Controllers/HomeController.cs",
            "src/MyApp.Business/Services/OrderService.cs",
            "src/MyApp.Data/Repositories/OrderRepository.cs",
        };

        var context = ArchitectureStyleDetector.Detect(paths);

        Assert.Equal(ArchitectureStyles.Layered, context.DetectedStyle);
        Assert.True(context.LayerFolderMap.ContainsKey("Presentation"));
        Assert.True(context.LayerFolderMap.ContainsKey("Business"));
        Assert.True(context.LayerFolderMap.ContainsKey("Data"));
    }

    [Fact]
    public void Detect_hexagonal_from_ports_and_adapters()
    {
        var paths = new[]
        {
            "src/Shop.Domain/Order.cs",
            "src/Shop.Ports/IOrderRepository.cs",
            "src/Shop.Adapters/Persistence/OrderRepository.cs",
        };

        var context = ArchitectureStyleDetector.Detect(paths);

        Assert.Equal(ArchitectureStyles.Hexagonal, context.DetectedStyle);
        Assert.True(context.LayerFolderMap.ContainsKey("Ports"));
        Assert.True(context.LayerFolderMap.ContainsKey("Adapters"));
    }

    [Fact]
    public void Detect_modular_monolith_from_modules_folder()
    {
        var paths = new[]
        {
            "src/Modules/Orders/Domain/Order.cs",
            "src/Modules/Orders/Application/CreateOrder.cs",
            "src/Modules/Billing/Domain/Invoice.cs",
            "src/Modules/Billing/Application/IssueInvoice.cs",
        };

        var context = ArchitectureStyleDetector.Detect(paths);

        Assert.Equal(ArchitectureStyles.ModularMonolith, context.DetectedStyle);
        Assert.Contains(context.NotablePatterns, p => p.Contains("Orders", StringComparison.Ordinal));
        Assert.Contains(context.NotablePatterns, p => p.Contains("Billing", StringComparison.Ordinal));
    }
}
