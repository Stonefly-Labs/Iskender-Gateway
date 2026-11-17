namespace Iskender.Container.Tests.LocalEnvironment;

/// <summary>The clean copy holds the tracked files only, as a clean checkout (research R17).</summary>
public class LocalCheckoutTests
{
    [Fact]
    public async Task TheCopyHoldsOnlyTrackedFilesAsACleanCheckout()
    {
        RequireLocalTools.RequireBashAndGit();
        await using var checkout = await LocalCheckout.CreateAsync();

        Assert.True(File.Exists(Path.Combine(checkout.Root, "local", "compose.yaml")));
        Assert.False(Directory.Exists(Path.Combine(checkout.Root, "local", ".secrets")));
        var directories = Directory.EnumerateDirectories(checkout.Root, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToList();
        Assert.DoesNotContain("bin", directories);
        Assert.DoesNotContain("obj", directories);
        Assert.Equal("", await LocalCheckout.GitAsync(checkout.Root, "status", "--porcelain"));
        Assert.Matches("^[0-9a-f]{40}$", checkout.Head);
        var source = await Fixture.ImageBuild.RepositoryRootAsync();
        Assert.Equal(
            Mode(await LocalCheckout.GitAsync(source, "ls-files", "--stage", "local/scripts/lib/common.sh")),
            Mode(await LocalCheckout.GitAsync(checkout.Root, "ls-files", "--stage", "local/scripts/lib/common.sh")));
    }

    private static string Mode(string stage) => stage.Split(' ')[0];
}
