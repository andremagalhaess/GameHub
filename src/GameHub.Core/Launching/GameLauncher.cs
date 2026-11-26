using System.ComponentModel;
using System.Diagnostics;
using GameHub.Core.Data;
using GameHub.Core.Models;

namespace GameHub.Core.Launching;

/// <summary>Processo iniciado, abstraído para testes.</summary>
public interface ILaunchedProcess
{
    /// <summary>Completa quando o processo termina. Null se não for possível acompanhar.</summary>
    Task? WaitForExitAsync();
}

public interface IProcessStarter
{
    ILaunchedProcess? Start(ProcessStartInfo info);
}

public sealed class SystemProcessStarter : IProcessStarter
{
    public ILaunchedProcess? Start(ProcessStartInfo info)
    {
        var p = Process.Start(info);
        return p is null ? null : new Wrapped(p);
    }

    private sealed class Wrapped : ILaunchedProcess
    {
        private readonly Process _p;
        public Wrapped(Process p) => _p = p;
        public Task? WaitForExitAsync()
        {
            try { return _p.WaitForExitAsync().ContinueWith(_ => _p.Dispose(), TaskScheduler.Default); }
            catch (InvalidOperationException) { _p.Dispose(); return null; }
        }
    }
}

public enum LaunchOutcome { Started, MissingExecutable, NoExecutableConfigured, Cancelled, Failed }

public sealed record LaunchResult(LaunchOutcome Outcome, string? Message = null, Task<long?>? SessionMinutes = null);

/// <summary>
/// Inicia jogos SOMENTE quando o usuário clica em JOGAR. Mede o tempo da sessão sem polling
/// (aguarda o evento de saída do processo). Se o EXE fechar em menos de
/// <see cref="MinTrackedSeconds"/>, provavelmente era um launcher intermediário que abriu outro processo,
/// então a sessão NÃO é contada (nada de horas inventadas).
/// </summary>
public sealed class GameLauncher
{
    public static int MinTrackedSeconds { get; set; } = 60;

    private readonly GameRepository _games;
    private readonly IProcessStarter _starter;
    private readonly Func<DateTimeOffset> _now;

    public event EventHandler<long>? SessionRecorded;

    public GameLauncher(GameRepository games, IProcessStarter? starter = null, Func<DateTimeOffset>? clock = null)
    {
        _games = games;
        _starter = starter ?? new SystemProcessStarter();
        _now = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public static ProcessStartInfo BuildStartInfo(Game game)
    {
        var exe = game.ExecutablePath!;
        var workDir = !string.IsNullOrWhiteSpace(game.WorkingDirectory) && Directory.Exists(game.WorkingDirectory)
            ? game.WorkingDirectory
            : Path.GetDirectoryName(exe) ?? "";
        return new ProcessStartInfo
        {
            FileName = exe,
            Arguments = game.LaunchArguments ?? "",
            WorkingDirectory = workDir,
            UseShellExecute = true, // respeita manifestos de elevação (UAC) e associações do Windows
        };
    }

    public LaunchResult Launch(Game game)
    {
        if (string.IsNullOrWhiteSpace(game.ExecutablePath))
            return new(LaunchOutcome.NoExecutableConfigured, "Este jogo ainda não tem executável. Edite o jogo e escolha o .exe.");
        if (!File.Exists(game.ExecutablePath))
            return new(LaunchOutcome.MissingExecutable, $"O executável não foi encontrado:\n{game.ExecutablePath}\nO jogo pode ter sido movido ou desinstalado.");

        ILaunchedProcess? proc;
        var started = _now();
        try
        {
            proc = _starter.Start(BuildStartInfo(game));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new(LaunchOutcome.Cancelled, "Inicialização cancelada (permissão de administrador negada).");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return new(LaunchOutcome.Failed, $"Não foi possível iniciar o jogo: {ex.Message}");
        }

        _games.MarkLaunched(game.Id, started);
        var wait = proc?.WaitForExitAsync();
        if (wait is null) return new(LaunchOutcome.Started);

        var session = TrackAsync(game.Id, started, wait);
        return new(LaunchOutcome.Started, null, session);
    }

    private async Task<long?> TrackAsync(long gameId, DateTimeOffset started, Task wait)
    {
        try { await wait.ConfigureAwait(false); }
        catch { return null; }
        var ended = _now();
        if ((ended - started).TotalSeconds < MinTrackedSeconds) return null;
        _games.RecordSession(gameId, started, ended);
        SessionRecorded?.Invoke(this, gameId);
        return (long)Math.Floor((ended - started).TotalMinutes);
    }
}
