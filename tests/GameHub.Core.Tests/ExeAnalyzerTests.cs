using GameHub.Core.Executables;

namespace GameHub.Core.Tests;

public class ExeAnalyzerTests
{
    private sealed class FakeReader : IExeVersionReader
    {
        private readonly ExeVersionInfo _info;
        public FakeReader(string? product = null, string? desc = null, string? company = null, string? original = null) =>
            _info = new ExeVersionInfo(product, desc, company, original, "1.0", "1.0");
        public ExeVersionInfo Read(string path) => _info;
    }

    [Theory]
    [InlineData("Cyberpunk2077.exe", "Cyberpunk 2077")]
    [InlineData("HollowKnight.exe", "Hollow Knight")]
    [InlineData("eldenring.exe", "eldenring")]
    [InlineData("Stray-Win64-Shipping.exe", "Stray")]
    [InlineData("witcher3.exe", "witcher 3")]
    [InlineData("DOOMEternal.exe", "DOOM Eternal")]
    [InlineData("Hades_II.exe", "Hades II")]
    [InlineData("Half-Life 2", "Half-Life 2")]
    [InlineData("Portal™ 2", "Portal 2")]
    [InlineData("Wolfenstein3D.exe", "Wolfenstein 3D")]
    [InlineData("GTA5.exe", "GTA 5")]
    public void CleanName_splits_and_strips(string raw, string expected) =>
        Assert.Equal(expected, ExeAnalyzer.CleanName(raw));

    [Fact]
    public void Suggests_from_file_name_when_no_version_info()
    {
        var a = new ExeAnalyzer(new FakeReader()).Analyze(@"C:\Games\bin\x64\Cyberpunk2077.exe");
        Assert.Equal("Cyberpunk 2077", a.SuggestedName);
    }

    [Fact]
    public void Prefers_product_name()
    {
        var a = new ExeAnalyzer(new FakeReader(product: "Cyberpunk 2077", company: "CD PROJEKT RED"))
            .Analyze(@"D:\SteamLibrary\steamapps\common\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe");
        Assert.Equal("Cyberpunk 2077", a.SuggestedName);
        Assert.Equal("Product Name", a.SuggestionSource);
        Assert.Equal("Cyberpunk 2077", a.FolderName);
    }

    [Fact]
    public void Ignores_engine_product_names_and_uses_folder()
    {
        // Jogos Unity/Unreal costumam ter Product Name genérico.
        var a = new ExeAnalyzer(new FakeReader(product: "UnityPlayer", desc: "Unity Player", company: "Unity Technologies"))
            .Analyze(@"C:\Games\Hollow Knight\hollow_knight.exe");
        Assert.Equal("Hollow Knight", a.SuggestedName);
        Assert.Equal("Pasta de instalação", a.SuggestionSource);
    }

    [Fact]
    public void Skips_generic_folders_like_Binaries_Win64()
    {
        var a = new ExeAnalyzer(new FakeReader(product: "BootstrapPackagedGame"))
            .Analyze(@"E:\Epic Games\Stray\Hk_project\Binaries\Win64\Stray-Win64-Shipping.exe");
        Assert.Equal("Hk project", a.FolderName == "Hk_project" ? "Hk project" : a.FolderName);
        Assert.Contains("Stray", a.Candidates);
    }

    [Fact]
    public void Ignores_product_name_equal_to_company()
    {
        var a = new ExeAnalyzer(new FakeReader(product: "Valve", company: "Valve"))
            .Analyze(@"C:\Games\Portal 2\portal2.exe");
        Assert.Equal("Portal 2", a.SuggestedName);
    }

    [Fact]
    public void Rejects_non_exe() =>
        Assert.Throws<ArgumentException>(() => new ExeAnalyzer(new FakeReader()).Analyze(@"C:\x\readme.txt"));

    [Fact]
    public void Analysis_never_executes_the_file()
    {
        // O analisador só lê metadados; um caminho inexistente não pode causar execução nem exceção.
        var a = new ExeAnalyzer().Analyze(Path.Combine(Path.GetTempPath(), "nao-existe", "bin", "MeuJogo.exe"));
        Assert.Contains("Meu Jogo", a.Candidates);
    }
}
