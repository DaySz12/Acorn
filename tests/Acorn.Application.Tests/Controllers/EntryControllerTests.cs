using Acorn.Application.ViewModels;
using Acorn.Core;
using Acorn.Core.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Acorn.Application.Tests.Controllers;

public class EntryControllerTests
{
    private const string EntryPassword = "Entry-Pass-7f3a9c";
    private const string FieldSecret = "token-55e1c0";

    private static Entry Web() => new()
    {
        Name = "web-01",
        Type = EntryTypes.Server,
        Env = "prod",
        Host = "10.0.0.5",
        Port = 2222,
        Username = "deploy",
        Password = EntryPassword,
        Tags = ["web", "nginx"],
        CustomFields =
        [
            new CustomField { Label = "API token", Value = FieldSecret, IsSecret = true },
            new CustomField { Label = "Region", Value = "bkk-1", IsSecret = false },
        ],
    };

    private static Entry Db() => new() { Name = "db-main", Env = "staging", Host = "db.internal", Username = "postgres", Tags = ["sql"], Favorite = true };

    private static async Task<(ControllerHarness H, Entry Web, Entry Db)> UnlockedWithEntriesAsync()
    {
        var h = new ControllerHarness();
        var web = Web();
        var db = Db();
        h.Data.Entries.AddRange([web, db]);
        await h.UnlockAsync();
        return (h, web, db);
    }

    [Fact]
    public async Task Copy_password_goes_to_clipboard_as_secret_is_cleared_later_and_is_not_returned()
    {
        var (h, web, _) = await UnlockedWithEntriesAsync();
        using var _h = h;

        Result result = await h.Entries.CopyPasswordAsync(web.Id);

        Assert.True(result.Succeeded);
        Assert.IsNotType<Result<string>>(result);
        await h.Clipboard.Received(1).SetTextAsync(EntryPassword, isSecret: true);
        h.Time.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(EntryPassword, h.ClipboardContent);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(h.ClipboardContent);
    }

    [Fact]
    public async Task Clipboard_timeout_follows_settings()
    {
        var h = new ControllerHarness();
        using var _h = h;
        var web = Web();
        h.Data.Entries.Add(web);
        h.Data.Settings.ClipboardClearSeconds = 10;
        await h.UnlockAsync();

        await h.Entries.CopyPasswordAsync(web.Id);
        h.Time.Advance(TimeSpan.FromSeconds(10));

        Assert.Null(h.ClipboardContent);
    }

    [Theory]
    [InlineData(CopyField.Host, "10.0.0.5")]
    [InlineData(CopyField.Username, "deploy")]
    [InlineData(CopyField.SshCommand, "ssh deploy@10.0.0.5 -p 2222")]
    public async Task Non_secret_fields_are_copied_without_clear_timer(CopyField field, string expected)
    {
        var (h, web, _) = await UnlockedWithEntriesAsync();
        using var _h = h;

        var result = await h.Entries.CopyAsync(web.Id, field);

        Assert.True(result.Succeeded);
        await h.Clipboard.Received(1).SetTextAsync(expected, isSecret: false);
        h.Time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(expected, h.ClipboardContent);
    }

    [Fact]
    public async Task Secret_custom_field_copy_is_treated_as_secret()
    {
        var (h, web, _) = await UnlockedWithEntriesAsync();
        using var _h = h;

        await h.Entries.CopySecretAsync(SecretRef.Field(web.Id, web.CustomFields[0].Id));

        await h.Clipboard.Received(1).SetTextAsync(FieldSecret, isSecret: true);
    }

    [Fact]
    public async Task Reveal_works_only_while_unlocked_and_hides_after_timeout()
    {
        var h = new ControllerHarness();
        using var _h = h;
        var web = Web();
        h.Data.Entries.Add(web);
        h.Data.Settings.RevealSeconds = 12;
        var secret = SecretRef.Password(web.Id);

        var locked = await h.Entries.RevealSecretAsync(secret);
        Assert.False(locked.Succeeded);
        Assert.Null(locked.Value);

        await h.UnlockAsync();
        var revealed = await h.Entries.RevealSecretAsync(secret);

        Assert.Equal(EntryPassword, revealed.Value);
        Assert.Equal(EntryPassword, h.State.RevealedValue(secret));
        h.Time.Advance(TimeSpan.FromSeconds(11));
        Assert.True(h.State.IsRevealed(secret));
        h.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(h.State.IsRevealed(secret));
    }

    [Fact]
    public async Task Revealed_secrets_are_hidden_on_navigation_selection_and_lock()
    {
        var (h, web, db) = await UnlockedWithEntriesAsync();
        using var _h = h;
        var secret = SecretRef.Password(web.Id);

        await h.Entries.RevealSecretAsync(secret);
        h.Entries.Select(db.Id);
        Assert.Empty(h.State.RevealedSecrets);

        await h.Entries.RevealSecretAsync(secret);
        h.Settings.Open();
        Assert.Empty(h.State.RevealedSecrets);
        h.Settings.Close();

        await h.Entries.RevealSecretAsync(secret);
        await h.Vault.LockAsync();
        Assert.Empty(h.State.RevealedSecrets);
    }

    [Fact]
    public async Task List_and_detail_view_models_never_contain_secret_values()
    {
        var (h, web, _) = await UnlockedWithEntriesAsync();
        using var _h = h;
        h.Entries.Select(web.Id);

        foreach (var item in h.State.EntryList.Items)
        {
            AssertNoSecretInStrings(item);
        }
        AssertNoSecretInStrings(h.State.SelectedEntry!);
        Assert.All(h.State.SelectedEntry!.CustomFields, AssertNoSecretInStrings);
        Assert.True(h.State.EntryList.Items.Single(i => i.Id == web.Id).HasPassword);
        Assert.Equal("bkk-1", h.State.SelectedEntry!.CustomFields[1].Value);
        Assert.Null(h.State.SelectedEntry!.CustomFields[0].Value);
    }

    private static void AssertNoSecretInStrings(object viewModel)
    {
        foreach (var property in viewModel.GetType().GetProperties())
        {
            var text = property.GetValue(viewModel) switch
            {
                string s => s,
                IEnumerable<string> list => string.Join("|", list),
                _ => null,
            };
            Assert.False(text?.Contains(EntryPassword, StringComparison.Ordinal) ?? false, property.Name);
            Assert.False(text?.Contains(FieldSecret, StringComparison.Ordinal) ?? false, property.Name);
        }
    }

    [Fact]
    public async Task Search_and_filters_narrow_the_list()
    {
        var (h, _, _) = await UnlockedWithEntriesAsync();
        using var _h = h;
        Assert.Equal(2, h.State.EntryList.Items.Count);

        h.Entries.SetSearch("NGINX");
        Assert.Equal("web-01", Assert.Single(h.State.EntryList.Items).Name);

        h.Entries.SetSearch("2222");
        Assert.Single(h.State.EntryList.Items);

        h.Entries.SetSearch(EntryPassword);
        Assert.Empty(h.State.EntryList.Items);

        h.Entries.SetSearch("");
        h.Entries.SetFilter(EntryFilter.ForEnvironment("staging"));
        Assert.Equal("db-main", Assert.Single(h.State.EntryList.Items).Name);

        h.Entries.SetFilter(EntryFilter.ForTag("web"));
        Assert.Equal("web-01", Assert.Single(h.State.EntryList.Items).Name);

        h.Entries.SetFilter(EntryFilter.Favorites);
        Assert.Equal("db-main", Assert.Single(h.State.EntryList.Items).Name);

        Assert.Equal(2, h.State.Sidebar.AllCount);
        Assert.Equal(1, h.State.Sidebar.FavoriteCount);
        Assert.Contains(h.State.Sidebar.Tags, t => t.Name == "sql" && t.Count == 1);
    }

    [Fact]
    public async Task Creating_an_entry_validates_then_saves_and_selects_it()
    {
        var (h, _, _) = await UnlockedWithEntriesAsync();
        using var _h = h;
        h.Entries.BeginCreate();
        var editor = h.State.Editor!;
        editor.PortText = "99999";

        var invalid = await h.Entries.SaveAsync();

        Assert.False(invalid.Succeeded);
        Assert.Contains(Messages.EntryNameRequired, editor.Errors);
        Assert.Contains(Messages.PortInvalid, editor.Errors);
        h.Session.DidNotReceive().Save();

        editor.Name = "  cache  ";
        editor.PortText = "6379";
        editor.Env = "Dev";
        editor.TagsText = "redis, cache, Redis";
        editor.NewPassword = "generated-secret";
        var saved = await h.Entries.SaveAsync();

        Assert.True(saved.Succeeded);
        h.Session.Received(1).Save();
        var entry = h.Data.Entries.Single(e => e.Name == "cache");
        Assert.Equal(6379, entry.Port);
        Assert.Equal("dev", entry.Env);
        Assert.Equal(["redis", "cache"], entry.Tags);
        Assert.Equal("generated-secret", entry.Password);
        Assert.Null(h.State.Editor);
        Assert.Equal(entry.Id, h.State.SelectedEntryId);
    }

    [Fact]
    public async Task Editing_keeps_stored_secrets_unless_replaced_or_removed()
    {
        var (h, web, _) = await UnlockedWithEntriesAsync();
        using var _h = h;

        h.Entries.BeginEdit(web.Id);
        var editor = h.State.Editor!;
        Assert.True(editor.HasExistingPassword);
        Assert.Equal("", editor.NewPassword);
        Assert.Equal("", editor.CustomFields[0].Value);
        Assert.True(editor.CustomFields[0].HasExistingSecret);
        editor.Notes = "rotated nothing";
        Assert.True((await h.Entries.SaveAsync()).Succeeded);

        var stored = h.Data.Entries.Single(e => e.Id == web.Id);
        Assert.Equal(EntryPassword, stored.Password);
        Assert.Equal(FieldSecret, stored.CustomFields[0].Value);

        h.Entries.BeginEdit(web.Id);
        h.State.Editor!.RemovePassword = true;
        await h.Entries.SaveAsync();
        Assert.Equal("", h.Data.Entries.Single(e => e.Id == web.Id).Password);
    }

    [Fact]
    public async Task Failed_write_rolls_back_the_in_memory_change()
    {
        var (h, web, _) = await UnlockedWithEntriesAsync();
        using var _h = h;
        h.Session.Save().Throws(new IOException("disk full"));

        h.Entries.BeginEdit(web.Id);
        h.State.Editor!.Name = "renamed";
        var edit = await h.Entries.SaveAsync();
        var delete = await h.Entries.DeleteAsync(web.Id);

        Assert.Equal(Messages.StorageError, edit.Error);
        Assert.Equal(Messages.StorageError, delete.Error);
        Assert.Equal("web-01", h.Data.Entries.Single(e => e.Id == web.Id).Name);
        Assert.NotNull(h.State.Editor);
    }

    [Fact]
    public async Task Delete_and_favorite_are_persisted()
    {
        var (h, web, db) = await UnlockedWithEntriesAsync();
        using var _h = h;
        h.Entries.Select(web.Id);

        Assert.True((await h.Entries.ToggleFavoriteAsync(web.Id)).Succeeded);
        Assert.True(web.Favorite);
        Assert.True((await h.Entries.DeleteAsync(web.Id)).Succeeded);

        Assert.DoesNotContain(h.Data.Entries, e => e.Id == web.Id);
        Assert.Null(h.State.SelectedEntryId);
        h.Session.Received(2).Save();
        Assert.Equal(db.Id, Assert.Single(h.State.EntryList.Items).Id);
    }

    [Fact]
    public async Task Generator_fills_the_editor_with_requested_length()
    {
        var (h, _, _) = await UnlockedWithEntriesAsync();
        using var _h = h;
        h.Entries.BeginCreate();
        h.State.Generator.Length = 32;

        Assert.True(h.Entries.GeneratePasswordForEditor().Succeeded);
        Assert.Equal(32, h.State.Editor!.NewPassword.Length);

        h.State.Generator.Lowercase = h.State.Generator.Uppercase = h.State.Generator.Digits = h.State.Generator.Symbols = false;
        Assert.Equal(Messages.GeneratorNeedsCharacterSet, h.Entries.GeneratePasswordForEditor().Error);
    }

    [Fact]
    public async Task Quick_search_focuses_search_only_when_unlocked()
    {
        var (h, _, _) = await UnlockedWithEntriesAsync();
        using var _h = h;
        h.Settings.Open();

        h.Entries.RequestQuickSearch();

        Assert.Equal(Screen.Vault, h.State.Screen);
        Assert.Equal(1, h.State.SearchFocusRequest);
        await h.Vault.LockAsync();
        h.Entries.RequestQuickSearch();
        Assert.Equal(Screen.Unlock, h.State.Screen);
    }
}
