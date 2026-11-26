namespace GameHub.Core.Metadata;

/// <summary>Um resultado de busca para o usuário escolher (nunca adicionado automaticamente).</summary>
public sealed class GameSearchResult
{
    public required string Provider { get; init; }
    public required string ProviderId { get; init; }
    public required string Name { get; init; }
    public int? Year { get; set; }
    public string? CoverUrl { get; set; }
    public string? Developer { get; set; }
    public string? ShortDescription { get; set; }
    /// <summary>Similaridade 0..1 com o termo buscado (só para ordenar, não para decidir).</summary>
    public double Score { get; set; }

    /// <summary>"2020 · CD PROJEKT RED" para exibição.</summary>
    public string Subtitle => string.Join(" · ", new[] { Year?.ToString(), Developer }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public enum ImageKind { Cover, Background, Logo, Screenshot, Icon }

public sealed record ImageCandidate(string Url, ImageKind Kind, string Provider, int? Width = null, int? Height = null);

/// <summary>Metadados completos de um jogo vindos de uma fonte online.</summary>
public sealed class GameMetadata
{
    public required string Provider { get; init; }
    public required string ProviderId { get; init; }
    public string Name { get; set; } = "";
    public List<string> AlternativeNames { get; set; } = new();
    public string? Description { get; set; }
    public string? ShortDescription { get; set; }
    public string? Developer { get; set; }
    public string? Publisher { get; set; }
    public List<string> Genres { get; set; } = new();
    public List<string> Platforms { get; set; } = new();
    public string? ReleaseDate { get; set; }
    public string? AgeRating { get; set; }
    public string? Website { get; set; }
    public string? TrailerUrl { get; set; }
    public double? UserRating { get; set; }
    public string? UserRatingSource { get; set; }
    public List<ImageCandidate> Images { get; set; } = new();
    /// <summary>Se o provedor é uma loja (ex.: Steam), o ID nela.</summary>
    public string? StoreId { get; set; }

    public IEnumerable<ImageCandidate> ImagesOf(ImageKind kind) => Images.Where(i => i.Kind == kind);
}

public interface IMetadataProvider
{
    /// <summary>Nome exibido (ex.: "Steam").</summary>
    string Name { get; }
    /// <summary>False quando faltam credenciais.</summary>
    bool IsConfigured { get; }
    Task<IReadOnlyList<GameSearchResult>> SearchAsync(string query, CancellationToken ct);
    Task<GameMetadata?> GetDetailsAsync(string providerId, CancellationToken ct);
}

/// <summary>Erro de um provedor, exibido ao usuário sem esconder os resultados dos demais.</summary>
public sealed record ProviderError(string Provider, string Message);

public sealed record MetadataSearchResponse(IReadOnlyList<GameSearchResult> Results, IReadOnlyList<ProviderError> Errors);
