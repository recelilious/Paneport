// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text;
using Paneport.Core.Logging;
using Paneport.Core.Security;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests.Security;

// S1.3.6: the token store over a fake secure item store. Write, read, overwrite, delete and a
// missing item behave as ITokenStore says; a corrupt item is reported and removed; and neither
// the token nor the account name reaches a captured log or a ToString result.
public sealed class SecureTokenStoreTests
{
    // Made-up values, built here so that no file holds a token's shape.
    private static readonly string s_account = "paneport" + "-tester";
    private static readonly string s_token = string.Concat(Enumerable.Repeat("x9", 24));

    [Fact]
    public async Task AMissingItemReadsAsNoSignIn()
    {
        var store = new SecureTokenStore(new FakeSecureItemStore(), new RecordingLog());

        Assert.Null(await store.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASignInRoundTripsUnderItsOneKey()
    {
        var items = new FakeSecureItemStore();
        var store = new SecureTokenStore(items, new RecordingLog());

        await store.WriteAsync(new SavedSignIn(s_account, new Secret(s_token)), TestContext.Current.CancellationToken);
        var signIn = await store.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal([SecureTokenStore.ItemKey], items.Items.Keys);
        Assert.NotNull(signIn);
        Assert.Equal(s_account, signIn.AccountName);
        Assert.Equal(s_token, signIn.RefreshToken.Reveal());
    }

    [Fact]
    public async Task AWriteReplacesTheSavedSignIn()
    {
        var store = new SecureTokenStore(new FakeSecureItemStore(), new RecordingLog());
        await store.WriteAsync(new SavedSignIn(s_account, new Secret(s_token)), TestContext.Current.CancellationToken);

        await store.WriteAsync(new SavedSignIn("another-account", new Secret("another-token-value")), TestContext.Current.CancellationToken);
        var signIn = await store.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("another-account", signIn?.AccountName);
        Assert.Equal("another-token-value", signIn?.RefreshToken.Reveal());
    }

    [Fact]
    public async Task DeleteRemovesTheSignInAndAMissingOneIsNoError()
    {
        var items = new FakeSecureItemStore();
        var store = new SecureTokenStore(items, new RecordingLog());
        await store.WriteAsync(new SavedSignIn(s_account, new Secret(s_token)), TestContext.Current.CancellationToken);

        await store.DeleteAsync(TestContext.Current.CancellationToken);
        await store.DeleteAsync(TestContext.Current.CancellationToken);

        Assert.Empty(items.Items);
        Assert.Null(await store.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"version\": 2, \"accountName\": \"a\", \"refreshToken\": \"b\"}")]
    [InlineData("{\"version\": 1, \"accountName\": \"\", \"refreshToken\": \"b\"}")]
    [InlineData("{\"version\": 1, \"accountName\": \"a\"}")]
    [InlineData("")]
    public async Task ACorruptItemIsReportedAndRemoved(string content)
    {
        var items = new FakeSecureItemStore();
        items.Plant(SecureTokenStore.ItemKey, Encoding.UTF8.GetBytes(content));
        var log = new RecordingLog();
        var store = new SecureTokenStore(items, log);

        Assert.Null(await store.ReadAsync(TestContext.Current.CancellationToken));

        Assert.Empty(items.Items);
        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal("The saved sign-in could not be read, so it was removed.", line.Message);
    }

    [Fact]
    public async Task NeitherTheTokenNorTheAccountNameReachesALogOrAText()
    {
        var items = new FakeSecureItemStore();
        var log = new RecordingLog();
        var store = new SecureTokenStore(items, log);
        var signIn = new SavedSignIn(s_account, new Secret(s_token));

        await store.WriteAsync(signIn, TestContext.Current.CancellationToken);
        var read = await store.ReadAsync(TestContext.Current.CancellationToken);
        items.Plant(SecureTokenStore.ItemKey, Encoding.UTF8.GetBytes($"{{\"version\": 9, \"accountName\": \"{s_account}\", \"refreshToken\": \"{s_token}\"}}"));
        await store.ReadAsync(TestContext.Current.CancellationToken);

        string[] texts = [signIn.ToString(), read!.ToString(), $"{signIn}", $"{read.RefreshToken}", .. log.Lines.Select(line => line.Message)];
        Assert.All(texts, text => Assert.DoesNotContain(s_token, text, StringComparison.Ordinal));
        Assert.All(texts, text => Assert.DoesNotContain(s_account, text, StringComparison.Ordinal));
        Assert.Equal("SavedSignIn [redacted]", signIn.ToString());
    }

    [Fact]
    public void ASignInNeedsAnAccountNameAndAToken()
    {
        Assert.Throws<ArgumentException>(() => new SavedSignIn(string.Empty, new Secret(s_token)));
        Assert.Throws<ArgumentNullException>(() => new SavedSignIn(s_account, null!));
    }
}
