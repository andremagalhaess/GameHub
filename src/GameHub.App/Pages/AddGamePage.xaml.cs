using GameHub.App.Helpers;
using GameHub.App.Services;
using GameHub.Core.Executables;
using GameHub.Core.Metadata;
using GameHub.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace GameHub.App.Pages;

/// <summary>
/// Fluxo: 1) escolher .EXE → nome sugerido; 2) BUSCAR INFORMAÇÕES ONLINE → escolher resultado;
/// 3) tela de confirmação com tudo editável → CONFIRMAR E ADICIONAR.
/// Também serve para "Atualizar informações online" de um jogo existente.
/// </summary>
public sealed partial class AddGamePage : Page
{
    private enum Step { Exe, Results, Confirm }

    private ExeAnalysis? _analysis;
    private Game? _existing;           // modo "atualizar informações" de um jogo já cadastrado
    private CancellationTokenSource? _cts;
    private Step _step;

    public AddGamePage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is RefreshMetadataRequest req && AppServices.Games.Get(req.GameId) is { } g)
        {
            _existing = g;
            PageTitle.Text = $"Atualizar informações · {g.Title}";
            SearchBox.Text = g.Title;
            _ = SearchAsync(g.Title);
        }
        else
        {
            ShowStep(Step.Exe);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _cts?.Cancel();
        base.OnNavigatedFrom(e);
    }

    private void ShowStep(Step step)
    {
        _step = step;
        StepExe.Visibility = step == Step.Exe ? Visibility.Visible : Visibility.Collapsed;
        StepResults.Visibility = step == Step.Results ? Visibility.Visible : Visibility.Collapsed;
        StepConfirm.Visibility = step == Step.Confirm ? Visibility.Visible : Visibility.Collapsed;
        ConfirmButton.Visibility = step == Step.Confirm ? Visibility.Visible : Visibility.Collapsed;
        NoneButton.Visibility = step == Step.Results ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = step != Step.Exe && !(step == Step.Results && _existing is not null) ? Visibility.Visible : Visibility.Collapsed;
        ConfirmText.Text = _existing is null ? "CONFIRMAR E ADICIONAR" : "SALVAR ALTERAÇÕES";
        var total = _existing is null ? 3 : 2;
        var index = _existing is null ? (int)step + 1 : (int)step;
        StepText.Text = step switch
        {
            Step.Exe => $"Passo 1 de 3 · Executável",
            Step.Results => $"Passo {index} de {total} · Escolha o jogo correto",
            _ => $"Passo {index} de {total} · Confira e edite as informações",
        };
    }

    // ---------- Passo 1 ----------

    private async void PickExe_Click(object sender, RoutedEventArgs e)
    {
        var path = await Pickers.PickExeAsync();
        if (path is null) return;
        SetBusy(true, "Analisando executável…");
        try
        {
            _analysis = await Task.Run(() => new ExeAnalyzer().Analyze(path));
        }
        catch (Exception ex)
        {
            await Dialogs.InfoAsync("Arquivo inválido", ex.Message);
            return;
        }
        finally { SetBusy(false); }

        var a = _analysis;
        NameBox.Text = a.SuggestedName;
        SuggestionSource.Text = $"Sugestão baseada em: {a.SuggestionSource}. Você pode editar o nome antes de buscar.";

        CandidatesPanel.Children.Clear();
        foreach (var c in a.Candidates.Where(c => c != a.SuggestedName).Take(4))
        {
            var b = new Button { Content = c, FontSize = 12, Padding = new Thickness(10, 4, 10, 4) };
            b.Click += (_, _) => NameBox.Text = c;
            CandidatesPanel.Children.Add(b);
        }

        ExeFacts.Children.Clear();
        AddFact("Arquivo", a.Path);
        AddFact("Product Name", a.Version.ProductName);
        AddFact("File Description", a.Version.FileDescription);
        AddFact("Company Name", a.Version.CompanyName);
        AddFact("Original Filename", a.Version.OriginalFilename);
        AddFact("Versão do arquivo", a.Version.FileVersion);
        AddFact("Versão do produto", a.Version.ProductVersion);
        AddFact("Pasta de instalação", a.FolderName);

        var dups = AppServices.Games.FindDuplicates(new Game { Title = a.SuggestedName, ExecutablePath = a.Path });
        DuplicateInfo.IsOpen = dups.Count > 0;
        DuplicateInfo.Message = string.Join("\n", dups.Select(d => $"\"{d.Existing.Title}\" já está na biblioteca ({d.Reason})."));
        ExeDetails.Visibility = Visibility.Visible;
        NameBox.Focus(FocusState.Programmatic);
        NameBox.SelectAll();
    }

    private void AddFact(string label, string? value)
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, Opacity = 0.65 });
        var v = new TextBlock { Text = Fmt.Or(value), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        ExeFacts.Children.Add(g);
    }

    private void NoExe_Click(object sender, RoutedEventArgs e)
    {
        _analysis = null;
        OpenEditor(new Game { Title = "" }, null);
    }

    private void NameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) Search_Click(sender, e);
    }

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text)) return;
        SearchBox.Text = NameBox.Text.Trim();
        _ = SearchAsync(SearchBox.Text);
    }

    // ---------- Passo 2 ----------

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) SearchAgain_Click(sender, e);
    }

    private void SearchAgain_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SearchBox.Text)) _ = SearchAsync(SearchBox.Text.Trim());
    }

    private async Task SearchAsync(string query)
    {
        ShowStep(Step.Results);
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        ResultsList.ItemsSource = null;
        SearchInfo.IsOpen = false;
        if (!AppServices.Metadata.ConfiguredProviders.Any())
        {
            ShowSearchInfo(InfoBarSeverity.Warning, "Nenhuma fonte de dados ativa",
                "Ative a Steam ou configure RAWG/IGDB em Configurações › Integrações. Você também pode preencher manualmente.");
            return;
        }

        SearchingText.Text = $"Buscando \"{query}\" em {string.Join(", ", AppServices.Metadata.ConfiguredProviders.Select(p => p.Name))}…";
        SearchingPanel.Visibility = Visibility.Visible;
        try
        {
            var resp = await AppServices.Metadata.SearchAsync(query, ct);
            if (ct.IsCancellationRequested) return;
            ResultsList.ItemsSource = resp.Results;
            ResultsHint.Visibility = resp.Results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            var configured = AppServices.Metadata.ConfiguredProviders.Count();
            if (resp.Results.Count == 0 && resp.Errors.Count == configured)
                ShowSearchInfo(InfoBarSeverity.Error, "Não foi possível buscar online",
                    string.Join("\n", resp.Errors.Select(er => $"{er.Provider}: {er.Message}")) + "\nVocê pode adicionar manualmente e completar depois.");
            else if (resp.Results.Count == 0)
                ShowSearchInfo(InfoBarSeverity.Informational, "Nenhum resultado",
                    "Tente outro nome (por exemplo, sem subtítulo ou edição) ou preencha manualmente.");
            else if (resp.Errors.Count > 0)
                ShowSearchInfo(InfoBarSeverity.Warning, "Algumas fontes falharam",
                    string.Join("\n", resp.Errors.Select(er => $"{er.Provider}: {er.Message}")));
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!ct.IsCancellationRequested) SearchingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowSearchInfo(InfoBarSeverity severity, string title, string message)
    {
        SearchInfo.Severity = severity;
        SearchInfo.Title = title;
        SearchInfo.Message = message;
        SearchInfo.IsOpen = true;
    }

    private async void Result_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not GameSearchResult result) return;
        SetBusy(true, $"Carregando {result.Name}…");
        try
        {
            var md = await AppServices.Metadata.GetDetailsAsync(result, _cts?.Token ?? default);
            if (md is null)
            {
                ShowSearchInfo(InfoBarSeverity.Warning, "Detalhes indisponíveis", $"{result.Provider} não retornou detalhes desse jogo. Escolha outro resultado.");
                return;
            }
            var g = _existing is not null ? Clone(_existing) : NewFromExe();
            MetadataService.ApplyTo(md, g);
            if (_existing is null && md.Provider == SteamStoreProvider.ProviderName &&
                g.ExecutablePath?.Contains(@"\steamapps\", StringComparison.OrdinalIgnoreCase) == true)
            {
                g.Source = GameSources.Steam;
                g.SourceId = md.StoreId;
            }
            OpenEditor(g, md);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowSearchInfo(InfoBarSeverity.Error, "Erro ao carregar detalhes", ex.Message);
        }
        finally { SetBusy(false); }
    }

    private void Manual_Click(object sender, RoutedEventArgs e)
    {
        var g = _existing is not null ? Clone(_existing) : NewFromExe();
        if (_existing is null && !string.IsNullOrWhiteSpace(NameBox.Text)) g.Title = NameBox.Text.Trim();
        OpenEditor(g, null);
    }

    private Game NewFromExe() => new()
    {
        Title = NameBox.Text.Trim(),
        ExecutablePath = _analysis?.Path,
        Source = GameSources.Manual,
    };

    // ---------- Passo 3 ----------

    private void OpenEditor(Game g, GameMetadata? md)
    {
        Editor.Load(g, md);
        ShowStep(Step.Confirm);
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (Editor.Validate() is { } error) { Editor.ShowError(error); return; }
        SetBusy(true, "Baixando artes e salvando…");
        ConfirmButton.IsEnabled = false;
        try
        {
            var warnings = new List<string>();
            var g = await Editor.CommitAsync(AppServices.Images, warnings);

            if (_existing is null)
            {
                var dups = AppServices.Games.FindDuplicates(g);
                if (dups.Count > 0)
                {
                    var ok = await Dialogs.ConfirmAsync("Esse jogo parece já estar na biblioteca",
                        string.Join("\n", dups.Select(d => $"• {d.Existing.Title} ({d.Reason})")) + "\n\nAdicionar mesmo assim?",
                        "Adicionar mesmo assim");
                    if (!ok) return;
                }
                AppServices.Games.Add(g);
            }
            else
            {
                AppServices.Games.Update(g);
            }

            if (warnings.Count > 0) await Dialogs.InfoAsync("Jogo salvo com avisos", string.Join("\n", warnings));
            if (_existing is not null) App.MainWindow!.GoBackOr(typeof(LibraryPage));
            else App.MainWindow!.ReplaceCurrent(typeof(GameDetailsPage), g.Id);
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao adicionar jogo", ex);
            Editor.ShowError($"Não foi possível salvar: {ex.Message}");
        }
        finally
        {
            ConfirmButton.IsEnabled = true;
            SetBusy(false);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step == Step.Confirm && ResultsList.ItemsSource is not null) ShowStep(Step.Results);
        else if (_existing is null) ShowStep(Step.Exe);
        else ShowStep(Step.Results);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        App.MainWindow!.GoBackOr(typeof(LibraryPage));
    }

    private void SetBusy(bool busy, string text = "")
    {
        Busy.IsActive = busy;
        BusyText.Text = busy ? text : "";
        ResultsList.IsEnabled = !busy;
    }

    private static Game Clone(Game g) => new()
    {
        Id = g.Id, Title = g.Title, AlternativeNames = g.AlternativeNames, ShortDescription = g.ShortDescription,
        Description = g.Description, Developer = g.Developer, Publisher = g.Publisher, Genres = g.Genres.ToList(),
        Tags = g.Tags.ToList(), Platforms = g.Platforms.ToList(), ReleaseDate = g.ReleaseDate, AgeRating = g.AgeRating,
        Website = g.Website, TrailerUrl = g.TrailerUrl, ExternalRating = g.ExternalRating, ExternalRatingSource = g.ExternalRatingSource,
        CoverPath = g.CoverPath, BackgroundPath = g.BackgroundPath, LogoPath = g.LogoPath, IconPath = g.IconPath,
        ExecutablePath = g.ExecutablePath, LaunchArguments = g.LaunchArguments, WorkingDirectory = g.WorkingDirectory,
        Source = g.Source, SourceId = g.SourceId, MetadataProvider = g.MetadataProvider, MetadataId = g.MetadataId,
        Status = g.Status, IsFavorite = g.IsFavorite, PersonalRating = g.PersonalRating, PlaytimeMinutes = g.PlaytimeMinutes,
        LastPlayedAt = g.LastPlayedAt, AddedAt = g.AddedAt, UpdatedAt = g.UpdatedAt,
    };
}
