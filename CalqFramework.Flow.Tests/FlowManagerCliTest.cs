using System.Text.Json;
using CalqFramework.Cli;

namespace CalqFramework.Flow.Tests;

/// <summary>
/// Tests that FlowManager integrates correctly with CalqFramework.Cli.
/// </summary>
public class FlowManagerCliTest {
    [Fact]
    public void Execute_Help_ReturnsWithoutError() {
        var cli = new CommandLineInterface() {
            InterfaceOut = new StringWriter()
        };

        var result = cli.Execute(new FlowManager(), new[] { "--help" });

        Assert.IsType<ValueTuple>(result);
    }

    [Fact]
    public void Execute_PublishHelp_ReturnsWithoutError() {
        var cli = new CommandLineInterface() {
            InterfaceOut = new StringWriter()
        };

        var result = cli.Execute(new FlowManager(), new[] { "publish", "--help" });

        Assert.IsType<ValueTuple>(result);
    }

    [Fact]
    public void Execute_Help_ContainsPublishSubcommand() {
        var output = new StringWriter();
        var cli = new CommandLineInterface() {
            InterfaceOut = output
        };

        cli.Execute(new FlowManager(), new[] { "--help" });

        var helpText = output.ToString();
        Assert.Contains("publish", helpText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_Help_ContainsGlobalOptions() {
        var output = new StringWriter();
        var cli = new CommandLineInterface() {
            InterfaceOut = output
        };

        cli.Execute(new FlowManager(), new[] { "--help" });

        var helpText = output.ToString();
        Assert.Contains("sources", helpText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remote", helpText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tag-prefix", helpText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_PublishHelp_ContainsParameters() {
        var output = new StringWriter();
        var cli = new CommandLineInterface() {
            InterfaceOut = output
        };

        cli.Execute(new FlowManager(), new[] { "publish", "--help" });

        var helpText = output.ToString();
        Assert.Contains("dry-run", helpText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sign", helpText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rolling-branch", helpText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ignore-access-modifiers", helpText, StringComparison.OrdinalIgnoreCase);
    }
}
