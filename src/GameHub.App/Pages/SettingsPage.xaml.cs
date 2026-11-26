using System.Diagnostics;
using GameHub.App.Helpers;
using GameHub.App.Services;
using GameHub.Core.Data;
using GameHub.Core.Metadata;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;

namespace GameHub.App.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _loading = true;

    public SettingsPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _loading = true;
        var s = AppServices.Settings;
        var theme = s.GetString(SettingKeys.Theme, "dark");
        ThemeRadios.SelectedIndex = theme switch { "light" => 1, "system" => 2, _ => 0 };
        TransparencyToggle.IsOn = ThemeService.TransparencyEnabled;
        AnimationsToggle.IsOn = ThemeService.AnimationsEnabled;
        CoverSlider.Value = ThemeService.CoverWidth;
        AccentPicker.Color = ThemeService.ParseColor(AppServices.Profiles.Get().AccentColor);
        SteamToggle.IsOn = s.GetBool(SettingKeys.SteamEnabled, true);
        RefreshKeyStates();
        BuildScanners();
        DataPathText.Text = $"Pasta de dados: {AppServices.Paths.Root}";
        CacheSizeText.Text = $"Imagens em cache: {AppServices.Images.SizeBytes() / 1024.0 / 1024.0:0.0} MB";
        VersionText.Text = $"GameHub {typeof(App).Assembly.GetName().Version?.ToString(3)} · banco de dados v{AppServices.Db.SchemaVersion}";
        _loading = false;
    }

    private void RefreshKeyStates()
    {
        var secrets = AppServices.Secrets;
        RawgState.Text = secrets.Has(SettingKeys.RawgApiKey) ? "Configurada ✓" : "Não configurada";
        var igdb = secrets.Has(SettingKeys.IgdbClientId) && secrets.Has(SettingKeys.IgdbClientSecret);
        IgdbState.Text = igdb ? "Configurado ✓" : "Não configurado";
        IgdbId.Text = secrets.Get(SettingKeys.IgdbClientId) ?? "";
        RawgKey.Password = "";
        IgdbSecret.Password = "";
        SteamApiState.Text = secrets.Has(SettingKeys.SteamWebApiKey) && !string.IsNullOrWhiteSpace(AppServices.Settings.Get(SettingKeys.SteamId))
            ? "Configurada ✓" : "Não configurada";
        SteamIdBox.Text = AppServices.Settings.Get(SettingKeys.SteamId) ?? "";
        SteamKeyBox.Password = "";
        IgnoredText.Text = $"Jogos removidos que não voltam na importação: {AppServices.Sources.IgnoredCount()}";
    }

    private void BuildScanners()
    {
        ScannersPanel.Children.Clear();
        foreach (var scanner in AppServices.Importer.Scanners)
        {
            if (scanner is GameHub.Core.Platforms.SteamWebApiScanner) continue; // configurada abaixo
            var name = AppServices.ScannerName(scanner);
            var label = scanner is GameHub.Core.Platforms.UninstallRegistryScanner ? "EA app, Battle.net, Riot, Rockstar e Amazon Games" : name;
            var toggle = new ToggleSwitch { Header = label, IsOn = AppServices.IsScannerEnabled(scanner), OnContent = "Procurar", OffContent = "Ignorar" };
            toggle.Toggled += (_, _) => AppServices.Settings.SetBool(SettingKeys.PlatformEnabledPrefix + name, toggle.IsOn);
            ScannersPanel.Children.Add(toggle);
        }
    }

    private void SaveSteamApi_Click(object sender, RoutedEventArgs e)
    {
        var id = SteamIdBox.Text.Trim();
        if (id.Length != 17 || !id.All(char.IsDigit) || !id.StartsWith("7656"))
        {
            ShowInfo(InfoBarSeverity.Warning, "O SteamID64 tem 17 dígitos e começa com 7656. Ele aparece na URL do seu perfil ou em sites como steamid.io.");
            return;
        }
        if (string.IsNullOrWhiteSpace(SteamKeyBox.Password) && !AppServices.Secrets.Has(SettingKeys.SteamWebApiKey))
        {
            ShowInfo(InfoBarSeverity.Warning, "Cole a chave da Steam Web API.");
            return;
        }
        if (!string.IsNullOrWhiteSpace(SteamKeyBox.Password)) AppServices.Secrets.Set(SettingKeys.SteamWebApiKey, SteamKeyBox.Password);
        AppServices.Settings.Set(SettingKeys.SteamId, id);
        RefreshKeyStates();
        ShowInfo(InfoBarSeverity.Success, "Steam Web API salva. Use Biblioteca › Atualizar biblioteca para importar.");
    }

    private void RemoveSteamApi_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Secrets.Set(SettingKeys.SteamWebApiKey, null);
        AppServices.Settings.Set(SettingKeys.SteamId, null);
        RefreshKeyStates();
    }

    private void ClearIgnored_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Sources.ClearIgnored();
        RefreshKeyStates();
        ShowInfo(InfoBarSeverity.Success, "Pronto. Na próxima atualização da biblioteca, os jogos removidos podem voltar.");
    }

    // ---------- Aparência ----------

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || (ThemeRadios.SelectedItem as RadioButton)?.Tag is not string tag) return;
        AppServices.Settings.Set(SettingKeys.Theme, tag);
        App.MainWindow!.ApplyAppearance();
    }

    private void Transparency_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppServices.Settings.SetBool(SettingKeys.Transparency, TransparencyToggle.IsOn);
        App.MainWindow!.ApplyAppearance();
    }

    private void Animations_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) AppServices.Settings.SetBool(SettingKeys.Animations, AnimationsToggle.IsOn);
    }

    private void Cover_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_loading) AppServices.Settings.SetInt(SettingKeys.CoverSize, (int)e.NewValue);
    }

    private void ApplyAccent_Click(object sender, RoutedEventArgs e)
    {
        var p = AppServices.Profiles.Get();
        p.AccentColor = ThemeService.ToHex(AccentPicker.Color);
        AppServices.Profiles.Save(p);
        ThemeService.ApplyAccentLive(p.AccentColor);
        ShowInfo(InfoBarSeverity.Success, "Cor de destaque aplicada. Alguns controles do Windows só mudam de cor ao reabrir o GameHub.");
    }

    // ---------- Integrações ----------

    private void Steam_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) AppServices.Settings.SetBool(SettingKeys.SteamEnabled, SteamToggle.IsOn);
    }

    private void SaveRawg_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RawgKey.Password)) { ShowInfo(InfoBarSeverity.Warning, "Cole a chave da RAWG antes de salvar."); return; }
        AppServices.Secrets.Set(SettingKeys.RawgApiKey, RawgKey.Password);
        RefreshKeyStates();
        ShowInfo(InfoBarSeverity.Success, "Chave da RAWG salva (cifrada).");
    }

    private void RemoveRawg_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Secrets.Set(SettingKeys.RawgApiKey, null);
        RefreshKeyStates();
    }

    private async void TestRawg_Click(object sender, RoutedEventArgs e) => await TestAsync(RawgProvider.ProviderName);

    private void SaveIgdb_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(IgdbId.Text) || string.IsNullOrWhiteSpace(IgdbSecret.Password))
        {
            ShowInfo(InfoBarSeverity.Warning, "Informe o Client ID e o Client Secret.");
            return;
        }
        AppServices.Secrets.Set(SettingKeys.IgdbClientId, IgdbId.Text);
        AppServices.Secrets.Set(SettingKeys.IgdbClientSecret, IgdbSecret.Password);
        RefreshKeyStates();
        ShowInfo(InfoBarSeverity.Success, "Credenciais do IGDB salvas (cifradas).");
    }

    private void RemoveIgdb_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Secrets.Set(SettingKeys.IgdbClientId, null);
        AppServices.Secrets.Set(SettingKeys.IgdbClientSecret, null);
        RefreshKeyStates();
    }

    private async void TestIgdb_Click(object sender, RoutedEventArgs e) => await TestAsync(IgdbProvider.ProviderName);

    private async Task TestAsync(string providerName)
    {
        var provider = AppServices.Metadata.AllProviders.First(p => p.Name == providerName);
        if (!provider.IsConfigured) { ShowInfo(InfoBarSeverity.Warning, $"{providerName} ainda não está configurado."); return; }
        try
        {
            var results = await provider.SearchAsync("Portal 2", CancellationToken.None);
            ShowInfo(InfoBarSeverity.Success, $"{providerName} funcionando: {results.Count} resultados para \"Portal 2\".");
        }
        catch (Exception ex)
        {
            ShowInfo(InfoBarSeverity.Error, $"{providerName}: {ex.Message}");
        }
    }

    // ---------- Dados ----------

    private void OpenData_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", ArgumentList = { AppServices.Paths.Root }, UseShellExecute = false });

    private void CleanCache_Click(object sender, RoutedEventArgs e)
    {
        var profile = AppServices.Profiles.Get();
        var used = AppServices.Games.All()
            .SelectMany(g => new[] { g.CoverPath, g.BackgroundPath, g.LogoPath, g.IconPath })
            .Append(profile.AvatarPath).Append(profile.BannerPath)
            .Concat(AppServices.Memories.All().Select(m => m.ImagePath)); // screenshots das memórias nunca são apagadas
        var freed = AppServices.Images.RemoveUnused(used);
        CacheSizeText.Text = $"Imagens em cache: {AppServices.Images.SizeBytes() / 1024.0 / 1024.0:0.0} MB";
        ShowInfo(InfoBarSeverity.Success, $"{freed / 1024.0 / 1024.0:0.0} MB liberados.");
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var path = await Pickers.SaveFileAsync($"GameHub-backup-{DateTime.Now:yyyy-MM-dd}", "Backup do GameHub", ".zip");
        if (path is null) return;
        BackupBusy.IsActive = true;
        try
        {
            await AppServices.Backup.CreateAsync(path);
            ShowInfo(InfoBarSeverity.Success, $"Backup criado: {path}");
        }
        catch (Exception ex)
        {
            Log.Error("Falha no backup", ex);
            ShowInfo(InfoBarSeverity.Error, $"Não foi possível criar o backup: {ex.Message}");
        }
        finally { BackupBusy.IsActive = false; }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var path = await Pickers.PickFileAsync(".zip");
        if (path is null) return;
        var ok = await Dialogs.ConfirmAsync("Restaurar backup?",
            "Os dados atuais do GameHub serão substituídos pelos do backup. Antes disso, uma cópia de segurança do estado atual é salva na pasta de dados (backups).",
            "Restaurar", destructive: true);
        if (!ok) return;
        BackupBusy.IsActive = true;
        try
        {
            var safety = await AppServices.Backup.RestoreAsync(path);
            ThemeService.ApplyAccentLive(AppServices.Profiles.Get().AccentColor);
            ShowInfo(InfoBarSeverity.Success, $"Backup restaurado. Cópia do estado anterior: {safety}" +
                "\nChaves de API de outro computador/conta do Windows precisam ser informadas de novo.");
            if (!AppServices.Profiles.Get().IsCreated) App.MainWindow!.ShowWelcome();
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao restaurar", ex);
            ShowInfo(InfoBarSeverity.Error, $"Não foi possível restaurar: {ex.Message}");
        }
        finally { BackupBusy.IsActive = false; }
    }

    private void ShowInfo(InfoBarSeverity severity, string message)
    {
        Info.Severity = severity;
        Info.Message = message;
        Info.IsOpen = true;
    }
}
