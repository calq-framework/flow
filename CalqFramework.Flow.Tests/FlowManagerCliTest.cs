namespace CalqFramework.Flow.Tests;

/// <summary>
///     Tests that FlowManager integrates correctly with CalqFramework.Cli.
/// </summary>
public class FlowManagerCliTest {
    [Fact]
    public void Execute_Help_ReturnsWithoutError() {
        string[] args = ["--help"];
        var cli = new CommandLineInterface {
            InterfaceOut = new StringWriter()
        };

        object? result = cli.Execute(new FlowManager(), args);

        Assert.IsType<ValueTuple>(result);
    }

    [Fact]
    public void Execute_PublishHelp_ReturnsWithoutError() {
        string[] args = ["publish", "--help"];
        var cli = new CommandLineInterface {
            InterfaceOut = new StringWriter()
        };

        object? result = cli.Execute(new FlowManager(), args);

        Assert.IsType<ValueTuple>(result);
    }

    [Fact]
    public void Execute_Help_ContainsPublishSubcommand() {
        string[] args = ["--help"];
        var output = new StringWriter();
        var cli = new CommandLineInterface {
            InterfaceOut = output
        };

        cli.Execute(new FlowManager(), args);

        string helpText = output.ToString();
        Assert.Contains("publish", helpText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_Help_ContainsGlobalOptions() {
        string[] args = ["--help"];
        var output = new StringWriter();
        var cli = new CommandLineInterface {
            InterfaceOut = output
        };

        cli.Execute(new FlowManager(), args);

        string helpText = output.ToString();
        Assert.Contains("sources", helpText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remote", helpText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tag-prefix", helpText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_PublishHelp_ContainsParameters() {
        string[] args = ["publish", "--help"];
        var output = new StringWriter();
        var cli = new CommandLineInterface {
            InterfaceOut = output
        };

        cli.Execute(new FlowManager(), args);

        string helpText = output.ToString();
        Assert.Contains("dry-run", helpText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sign", helpText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rolling-branch", helpText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ignore-access-modifiers", helpText, StringComparison.OrdinalIgnoreCase);
    }
}
