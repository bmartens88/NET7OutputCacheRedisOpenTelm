namespace MyApi.Dto;

public record struct ProductDto(
    string Name,
    string Description,
    decimal Price);